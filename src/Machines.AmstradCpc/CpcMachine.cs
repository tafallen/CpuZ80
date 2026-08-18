using CpuZ80.Core;
using Machines.Common;
using Machines.ZxSpectrum128;

namespace Machines.AmstradCpc;

/// <summary>
/// Amstrad CPC 464 / 6128.
/// </summary>
/// <remarks>
/// See docs/amstrad-cpc.md. The two models differ in RAM (64K vs 128K), storage
/// and BASIC version; one class covers both.
/// </remarks>
public sealed class CpcMachine : ICpuHost
{
    /// <summary>4 MHz, and every access is aligned to a microsecond boundary.</summary>
    public const int ClockHz = 4_000_000;

    /// <summary>A 50 Hz frame at 4 MHz.</summary>
    public const int FrameCycles = 80_000;

    /// <summary>
    /// T-states between HSyncs: 64 microseconds per scanline at 4 MHz.
    /// </summary>
    private const int CyclesPerScanline = 256;

    public Cpu Cpu { get; }
    public CpcMemory Memory { get; }
    public AmstradGateArray GateArray { get; }
    public Mc6845 Crtc { get; }
    public Ppi8255 Ppi { get; }
    public Ay38912 Psg { get; }
    public CpcVideo Video { get; }

    private readonly AddressDecoder _bus;
    private readonly PortDecoder _ports;
    private readonly RomSelectPort _romSelect;
    private ulong _nextHSync;
    private ulong _frameEnd;

    public CpcMachine(
        byte[] lowerRom,
        byte[] upperRom,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        bool has128K = true)
    {
        _bus = new AddressDecoder();
        Memory = new CpcMemory(_bus, lowerRom, upperRom, has128K);

        GateArray = new AmstradGateArray(Memory);
        Crtc = new Mc6845();
        Psg = new Ay38912();

        ICpcKeyboard matrix = keyboard is not null
            ? new CpcKeyboard(keyboard)
            : NullCpcKeyboard.Instance;

        Ppi = new Ppi8255(Psg, matrix);
        Video = new CpcVideo(Crtc, GateArray, Memory);
        _romSelect = new RomSelectPort(Memory);

        // Devices are selected by individual address lines being low or high, so
        // addresses overlap and more than one can answer a single access.
        _ports = new PortDecoder(PortDecoder.ConflictPolicy.LogicalAnd);
        _ports.MapMirror(0x4000, 0xC000, 0xFFFF, GateArray);   // A15 clear, A14 set
        _ports.MapMirror(0x2000, 0x6000, 0xFFFF, Crtc);        // A14 clear, A13 set
        _ports.MapMirror(0x0000, 0x2000, 0xFFFF, _romSelect);  // A13 clear
        _ports.MapMirror(0x0000, 0x0800, 0xFFFF, Ppi);         // A11 clear

        Cpu = new Cpu(_bus, _ports, this)
        {
            // The Gate Array holds READY so no access completes off a
            // microsecond boundary. See US-500.
            AlignInstructionsTo4TStates = true,
        };

        GateArray.InterruptRequested += () => Cpu.IntPin = true;

        _audio = audio;
    }

    private readonly IAudioSink? _audio;

    public void Reset()
    {
        Cpu.Reset();
        Memory.Reset();
        GateArray.Reset();
        Crtc.Reset();
        Ppi.Reset();
        Psg.Reset();

        // The CPU boots in interrupt mode 1 with the lower ROM paged in.
        _nextHSync = Cpu.TotalCycles + CyclesPerScanline;

        // Not TotalCycles + FrameCycles: RunFrame adds a frame before running,
        // so pre-adding one here makes the first frame run twice as long as
        // every other one.
        _frameEnd = Cpu.TotalCycles;
        _lastAudioTState = Cpu.TotalCycles;
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort port) => _ports.In(port);
    public void WritePort(ushort port, byte value) => _ports.Out(port, value);

    public void Step() => Cpu.Step();

    /// <summary>Runs one 50 Hz frame, feeding the Gate Array its HSyncs as it goes.</summary>
    public void RunFrame()
    {
        _frameEnd += FrameCycles;

        // Recover rather than spin if the machine has fallen far behind.
        if (Cpu.TotalCycles > _frameEnd + FrameCycles)
        {
            _frameEnd = Cpu.TotalCycles + FrameCycles;
            _nextHSync = Cpu.TotalCycles + CyclesPerScanline;
        }

        // VSync is asserted for the last few scanlines of the frame, which is
        // what the firmware waits on.
        ulong vsyncStart = _frameEnd - (8 * CyclesPerScanline);

        while (Cpu.TotalCycles < _frameEnd)
        {
            if (Cpu.TotalCycles >= _nextHSync)
            {
                _nextHSync += CyclesPerScanline;
                GateArray.OnHSync();
            }

            bool vsync = Cpu.TotalCycles >= vsyncStart;

            // The Gate Array resynchronises its interrupt counter two HSyncs
            // after VSync begins, so it needs the leading edge, not the level.
            if (vsync && !Ppi.VSync) GateArray.OnVSync();
            Ppi.VSync = vsync;

            Cpu.Step();
        }

        Ppi.VSync = false;
    }

    /// <summary>The PSG is clocked at 1 MHz, a quarter of the CPU clock.</summary>
    public const int PsgClockHz = 1_000_000;

    private const int SampleRate = 44_100;

    private readonly short[] _audioBuffer = new short[2048];
    private ulong _lastAudioTState;

    public void RenderFrame(IVideoSink sink)
    {
        Video.Render(sink);

        if (_audio is null) return;

        ulong elapsed = Cpu.TotalCycles - _lastAudioTState;
        _lastAudioTState = Cpu.TotalCycles;
        if (elapsed == 0) return;

        int sampleCount = (int)Math.Min((ulong)_audioBuffer.Length, elapsed * SampleRate / ClockHz);
        if (sampleCount <= 0) return;

        var span = _audioBuffer.AsSpan(0, sampleCount);

        // Render expects PSG clock cycles, not CPU T-states. The CPC divides the
        // 4 MHz CPU clock by four to reach the PSG's 1 MHz, and passing T-states
        // straight through would run every channel four times too fast.
        Psg.Render(span, elapsed / (ClockHz / PsgClockHz));

        _audio.SubmitSamples(span, SampleRate);
    }

    // ── ICpuHost ─────────────────────────────────────────────────────────────

    public void OnMemoryAccess(ushort address, Cpu cpu) { }

    public void OnPortAccess(ushort port, Cpu cpu) { }

    /// <summary>
    /// The Gate Array holds INT asserted until the CPU acknowledges it. Leaving
    /// it asserted re-enters the handler the instant it returns and the main
    /// program never runs again — with a healthy stack and a plausible PC, so it
    /// does not look like a crash.
    /// </summary>
    public void OnInterruptAcknowledged(Cpu cpu)
    {
        cpu.IntPin = false;
        GateArray.OnInterruptAcknowledged();
    }

    /// <summary>
    /// The upper ROM select latch, on any port with A13 clear (&amp;DFxx).
    /// </summary>
    private sealed class RomSelectPort(CpcMemory memory) : IPortBus
    {
        public byte In(ushort port) => 0xFF;

        public void Out(ushort port, byte value) => memory.SelectUpperRom(value);
    }
}
