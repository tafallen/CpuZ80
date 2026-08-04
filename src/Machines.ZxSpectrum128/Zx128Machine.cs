using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum128;

/// <summary>
/// Sinclair ZX Spectrum 128 / +2 (grey) machine motherboard.
/// </summary>
/// <remarks>
/// Eight 16K RAM banks and two 16K ROMs in a 64K space, paged by port 0x7FFD.
/// Runs at 3.5469 MHz: 228 T-states per line, 311 lines, 70,908 per frame.
///
/// See docs/zx-spectrum-128.md for the hardware notes this is built from.
/// </remarks>
public sealed class Zx128Machine
{
    private const int BankSize = 0x4000;
    private const int BankCount = 8;
    private const int RomSize = 0x4000;

    public Cpu Cpu { get; }

    /// <summary>The eight 16K RAM banks, indexed as the hardware numbers them.</summary>
    public Ram[] Banks { get; }

    public Zx128MemoryPager Pager { get; }
    public FerrantiUla5C6C Ula { get; }

    /// <summary>The AY-3-8912 sound chip on ports 0xFFFD / 0xBFFD.</summary>
    public Ay38912 Ay { get; }

    /// <summary>False when only a 16K image was supplied and ROM 1 is absent.</summary>
    public bool Rom1Present { get; }

    private readonly AddressDecoder _bus;
    private readonly Zx128PortBus _ports;
    private readonly IAudioSink? _audioSink;
    private ulong _nextFrameTarget;
    private ulong _lastRenderTState;

    /// <summary>Scratch buffer for the AY, sized for one frame at 44.1 kHz.</summary>
    private readonly short[] _ayBuffer = new short[1024];

    /// <summary>
    /// Builds a 128. <paramref name="romImage"/> is either a 32K image holding
    /// ROM 0 followed by ROM 1, or a 16K image holding ROM 0 alone — in which
    /// case reads of ROM 1 return open bus and <see cref="Rom1Present"/> is false.
    /// </summary>
    public Zx128Machine(
        byte[] romImage,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        ITapeDevice? tape = null)
        : this(
            SplitRom0(romImage),
            SplitRom1(romImage),
            romImage.Length == RomSize * 2,
            keyboard, audio, tape)
    {
    }

    /// <summary>
    /// Builds a 128 from two separate 16K ROM images, as they are usually
    /// distributed (<c>128-0.rom</c> and <c>128-1.rom</c>).
    /// </summary>
    public Zx128Machine(
        byte[] rom0Image,
        byte[] rom1Image,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        ITapeDevice? tape = null)
        : this(
            Require16K(rom0Image, nameof(rom0Image)),
            Require16K(rom1Image, nameof(rom1Image)),
            rom1Present: true,
            keyboard, audio, tape)
    {
    }

    private Zx128Machine(
        byte[] rom0,
        byte[] rom1,
        bool rom1Present,
        IPhysicalKeyboard? keyboard,
        IAudioSink? audio,
        ITapeDevice? tape)
    {
        Rom1Present = rom1Present;

        Banks = new Ram[BankCount];
        for (int i = 0; i < BankCount; i++) Banks[i] = new Ram(BankSize);

        _bus = new AddressDecoder();
        Pager = new Zx128MemoryPager(_bus, Banks, [new Rom(rom0), new Rom(rom1)]);

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;

        // The ULA displays bank 5 at reset; US-455 will let it follow the pager.
        // Contention is delegated to the pager: 0xC000-0xFFFF contends only while
        // an odd bank is paged there, which the address alone cannot express.
        Ula = new FerrantiUla5C6C(
            Banks[5], kbAdapter, audio, tape,
            UlaTiming.Spectrum128,
            isContended: Pager.IsContended);

        Ay = new Ay38912();
        _audioSink = audio;

        var joystick = keyboard is not null ? new KempstonJoystick(keyboard) : null;
        _ports = new Zx128PortBus(Ula, Pager, Ay, joystick, new FerrantiUla5C6CBridge(() => Ula.FloatingBusValue));

        Cpu = new Cpu(_bus, _ports, Ula);
        Ula.ConnectCpu(Cpu);

        // Bit 3 of 0x7FFD moves the display between bank 5 and bank 7.
        Pager.PagingChanged += () => Ula.SetScreenSource(Banks[Pager.ScreenBank]);
    }

    /// <summary>A 16K ROM reading as open bus, standing in for an absent ROM 1.</summary>
    private static byte[] OpenBusRom()
    {
        byte[] image = new byte[RomSize];
        Array.Fill(image, (byte)0xFF);
        return image;
    }

    private static byte[] Require16K(byte[] image, string paramName)
    {
        if (image.Length != RomSize)
        {
            throw new ArgumentException($"ROM image must be {RomSize} bytes, got {image.Length}.", paramName);
        }
        return image;
    }

    private static byte[] SplitRom0(byte[] romImage)
    {
        ValidateCombined(romImage);
        return romImage[..RomSize];
    }

    private static byte[] SplitRom1(byte[] romImage)
    {
        ValidateCombined(romImage);
        return romImage.Length == RomSize * 2 ? romImage[RomSize..] : OpenBusRom();
    }

    private static void ValidateCombined(byte[] romImage)
    {
        if (romImage.Length != RomSize && romImage.Length != RomSize * 2)
        {
            throw new ArgumentException(
                $"ROM image must be {RomSize} bytes (ROM 0 only) or {RomSize * 2} bytes (ROM 0 + ROM 1), got {romImage.Length}.",
                nameof(romImage));
        }
    }

    public void Reset()
    {
        Cpu.Reset();
        Pager.Reset();
        Ula.Reset();
        Ay.Reset();
        _nextFrameTarget = Cpu.TotalCycles;
        _lastRenderTState = Cpu.TotalCycles;
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort address) => _ports.In(address);

    public void WritePort(ushort address, byte value)
    {
        Ula.OnPortAccess(address, Cpu);
        _ports.Out(address, value);
    }

    public void Step() => Cpu.Step();

    public void RunFrame()
    {
        ulong frameStart = Cpu.TotalCycles;
        Ula.OnFrameStart(frameStart);

        // 50 Hz maskable interrupt, held for a little over one instruction.
        Cpu.IntPin = true;
        ulong releaseIntAt = frameStart + 32;

        _nextFrameTarget += (ulong)Ula.Timing.FrameCycles;

        // Resynchronise if we have fallen more than a frame behind.
        if (Cpu.TotalCycles > _nextFrameTarget + (ulong)Ula.Timing.FrameCycles)
        {
            _nextFrameTarget = Cpu.TotalCycles + (ulong)Ula.Timing.FrameCycles;
        }

        while (Cpu.TotalCycles < _nextFrameTarget)
        {
            if (Cpu.IntPin && Cpu.TotalCycles >= releaseIntAt) Cpu.IntPin = false;
            Cpu.Step();
        }
    }

    public void RenderFrame(IVideoSink sink)
    {
        // The ULA renders video and the beeper; the AY is mixed in on top.
        Ula.RenderFrame(sink, Cpu.TotalCycles);

        if (_audioSink is not null)
        {
            ulong elapsed = Cpu.TotalCycles - _lastRenderTState;
            _lastRenderTState = Cpu.TotalCycles;

            if (elapsed > 0)
            {
                // One frame at 44.1 kHz is ~882 samples.
                int sampleCount = Math.Min(_ayBuffer.Length, (int)(elapsed * 44100 / 3546900));
                if (sampleCount > 0)
                {
                    var span = _ayBuffer.AsSpan(0, sampleCount);
                    Ay.Render(span, elapsed);
                    _audioSink.SubmitSamples(span, 44100);
                }
            }
        }
    }
}
