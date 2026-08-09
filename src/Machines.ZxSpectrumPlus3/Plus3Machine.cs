using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;
using Machines.ZxSpectrum;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrumPlus3;

/// <summary>
/// Sinclair ZX Spectrum +2A / +2B / +3 / +3B motherboard.
/// </summary>
/// <remarks>
/// Eight 16K RAM banks and four 16K ROMs, paged by ports 0x7FFD and 0x1FFD.
/// Same 228 T-state line and 70,908 T-state frame as the 128, but the drawn area
/// starts at 14,364, the contention sequence differs, and the gate array
/// contends memory only.
///
/// The +2A and +3 differ only in the presence of a disk drive, which is not
/// modelled yet — both boot to their menu without one. Note the +2 (grey) is a
/// different machine: it is a 128 in a new case and belongs to
/// <see cref="Zx128Machine"/>.
///
/// See docs/zx-spectrum-plus3.md.
/// </remarks>
public sealed class Plus3Machine
{
    private const int BankSize = 0x4000;
    private const int BankCount = 8;
    private const int RomSize = 0x4000;
    private const int RomCount = 4;

    public Cpu Cpu { get; }

    /// <summary>The eight 16K RAM banks, indexed as the hardware numbers them.</summary>
    public Ram[] Banks { get; }

    public Plus3MemoryPager Pager { get; }
    public FerrantiUla5C6C Ula { get; }

    /// <summary>The AY-3-8912 on ports 0xFFFD / 0xBFFD.</summary>
    public Ay38912 Ay { get; }

    /// <summary>
    /// The floppy controller, or null when no drive is fitted — which makes this
    /// machine a +2A rather than a +3.
    /// </summary>
    public Upd765a? Fdc { get; }

    private readonly AddressDecoder _bus;
    private readonly Plus3PortBus _ports;
    private readonly IAudioSink? _audioSink;
    private ulong _nextFrameTarget;
    private ulong _lastRenderTState;
    private readonly short[] _ayBuffer = new short[1024];

    /// <summary>
    /// Builds a +2A/+3 from a single 64K image holding ROMs 0-3 in order.
    /// </summary>
    public Plus3Machine(
        byte[] romImage,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        ITapeDevice? tape = null,
        bool diskDrive = false)
        : this(SplitRoms(romImage), keyboard, audio, tape, diskDrive)
    {
    }

    /// <summary>
    /// Builds a +2A/+3 from four separate 16K ROM images, in order: 128 editor,
    /// syntax checker, +3DOS, 48 BASIC.
    /// </summary>
    public Plus3Machine(
        byte[][] romImages,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        ITapeDevice? tape = null,
        bool diskDrive = false)
    {
        if (romImages.Length != RomCount)
        {
            throw new ArgumentException($"Expected {RomCount} ROM images, got {romImages.Length}.", nameof(romImages));
        }

        for (int i = 0; i < RomCount; i++)
        {
            if (romImages[i].Length != RomSize)
            {
                throw new ArgumentException(
                    $"ROM {i} must be {RomSize} bytes, got {romImages[i].Length}.", nameof(romImages));
            }
        }

        Banks = new Ram[BankCount];
        for (int i = 0; i < BankCount; i++) Banks[i] = new Ram(BankSize);

        _bus = new AddressDecoder();

        var roms = new Rom[RomCount];
        for (int i = 0; i < RomCount; i++) roms[i] = new Rom(romImages[i]);
        Pager = new Plus3MemoryPager(_bus, Banks, roms);

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;

        // Contention is delegated to the pager: which windows contend depends on
        // the banks currently mapped, and in special mode that includes 0x0000.
        Ula = new FerrantiUla5C6C(
            Banks[5], kbAdapter, audio, tape,
            UlaTiming.Spectrum2A,
            isContended: Pager.IsContended);

        Ay = new Ay38912();
        _audioSink = audio;

        if (diskDrive) Fdc = new Upd765a();

        var joystick = keyboard is not null ? new KempstonJoystick(keyboard) : null;
        _ports = new Plus3PortBus(Ula, Pager, Ay, joystick, () => Ula.FloatingBusValue, Fdc);

        Cpu = new Cpu(_bus, _ports, Ula);
        Ula.ConnectCpu(Cpu);

        // Bit 3 of 0x7FFD moves the display between bank 5 and bank 7.
        Pager.PagingChanged += () => Ula.SetScreenSource(Banks[Pager.ScreenBank]);

        // The drive reports not-ready with the motor off, so the FDC takes it
        // from the pager's decoded latch rather than sniffing the port itself.
        if (Fdc is not null)
        {
            Pager.MotorChanged += () => Fdc.MotorOn = Pager.MotorOn;
        }
    }

    private static byte[][] SplitRoms(byte[] romImage)
    {
        if (romImage.Length != RomSize * RomCount)
        {
            throw new ArgumentException(
                $"Combined ROM image must be {RomSize * RomCount} bytes ({RomCount} x {RomSize}), got {romImage.Length}.",
                nameof(romImage));
        }

        var roms = new byte[RomCount][];
        for (int i = 0; i < RomCount; i++) roms[i] = romImage[(i * RomSize)..((i + 1) * RomSize)];
        return roms;
    }

    public void Reset()
    {
        Cpu.Reset();
        Pager.Reset();
        Ula.Reset();
        Ay.Reset();
        Fdc?.Reset();
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

        Cpu.IntPin = true;
        ulong releaseIntAt = frameStart + 32;

        _nextFrameTarget += (ulong)Ula.Timing.FrameCycles;

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
        Ula.RenderFrame(sink, Cpu.TotalCycles);

        if (_audioSink is not null)
        {
            ulong elapsed = Cpu.TotalCycles - _lastRenderTState;
            _lastRenderTState = Cpu.TotalCycles;

            if (elapsed > 0)
            {
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
