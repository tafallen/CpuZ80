using CpuZ80.Core;
using Machines.Common;
using Machines.ZxSpectrum128;
using Machines.ZxSpectrumPlus3;

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

    /// <summary>
    /// T-states per CRTC character: one microsecond at 4 MHz.
    /// </summary>
    /// <remarks>
    /// Everything else about frame timing follows from this and the CRTC's own
    /// registers, rather than from constants. A line is R0+1 characters and a
    /// frame is (R4+1)(R9+1)+R5 lines, so reprogramming any of them changes the
    /// frame rate — which is exactly what raster effects and overscan screens
    /// rely on.
    /// </remarks>
    public const int CyclesPerCharacter = 4;

    /// <summary>A standard 50 Hz frame: 312 lines of 64 characters.</summary>
    public const int FrameCycles = 312 * 64 * CyclesPerCharacter;

    public Cpu Cpu { get; }
    public CpcMemory Memory { get; }
    public AmstradGateArray GateArray { get; }
    public Mc6845 Crtc { get; }
    public Ppi8255 Ppi { get; }
    public Ay38912 Psg { get; }
    public CpcVideo Video { get; }

    /// <summary>Which machine this is.</summary>
    public CpcModel Model { get; }

    /// <summary>
    /// The floppy controller, or null on a machine with no drive fitted.
    /// </summary>
    public Upd765a? Fdc { get; }

    /// <summary>The cassette deck, or null when none is attached.</summary>
    public ITapeDevice? Tape { get; }

    private readonly AddressDecoder _bus;
    private readonly PortDecoder _ports;
    private readonly RomSelectPort _romSelect;
    private ulong _nextLine;

    public CpcMachine(
        byte[] lowerRom,
        byte[] upperRom,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        bool has128K = true)
        : this(has128K ? CpcModel.Cpc6128 : CpcModel.Cpc464, lowerRom, upperRom, keyboard, audio)
    {
    }

    public CpcMachine(
        CpcModel model,
        byte[] lowerRom,
        byte[] upperRom,
        IPhysicalKeyboard? keyboard = null,
        IAudioSink? audio = null,
        ITapeDevice? tape = null)
    {
        Model = model;
        Tape = tape;

        _bus = new AddressDecoder();
        Memory = new CpcMemory(_bus, lowerRom, upperRom, model.Has128K());

        GateArray = new AmstradGateArray(Memory);
        Crtc = new Mc6845();
        Psg = new Ay38912();

        ICpcKeyboard matrix = keyboard is not null
            ? new CpcKeyboard(keyboard)
            : NullCpcKeyboard.Instance;

        Ppi = new Ppi8255(Psg);

        // Given the CPU's clock the controller models seek times and the disk's
        // data rate rather than completing instantly. The clock is attached
        // after the CPU exists.
        if (model.HasDiskDrive()) Fdc = new Upd765a { ClockHz = ClockHz };

        // The keyboard is wired to the PSG's I/O port A, with the row selected
        // by the PPI. Reading a key therefore goes CPU -> PPI -> PSG -> matrix,
        // which is why it is attached here rather than intercepted in the PPI.
        Psg.PortAInput = () => matrix.ReadRow(Ppi.SelectedKeyboardRow);
        Video = new CpcVideo(Crtc, GateArray, Memory);
        _romSelect = new RomSelectPort(Memory);

        // Devices are selected by individual address lines being low or high, so
        // addresses overlap and more than one can answer a single access.
        _ports = new PortDecoder(PortDecoder.ConflictPolicy.LogicalAnd);
        _ports.MapMirror(0x4000, 0xC000, 0xFFFF, GateArray);   // A15 clear, A14 set
        _ports.MapMirror(0x2000, 0x6000, 0xFFFF, Crtc);        // A14 clear, A13 set
        _ports.MapMirror(0x0000, 0x2000, 0xFFFF, _romSelect);  // A13 clear
        _ports.MapMirror(0x0000, 0x0800, 0xFFFF, Ppi);         // A11 clear

        // The floppy controller sits on A10, at &FA7E and &FB7E — not where the
        // +3 puts it, so it gets the CPC's own decoding.
        if (Fdc is not null) _ports.MapMirror(0x0000, 0x0400, 0xFFFF, new CpcFdcPort(Fdc));

        Cpu = new Cpu(_bus, _ports, this)
        {
            // The Gate Array holds READY so no access completes off a
            // microsecond boundary. See US-500.
            AlignInstructionsTo4TStates = true,
        };

        GateArray.InterruptRequested += () => Cpu.IntPin = true;

        if (Fdc is not null) Fdc.Clock = () => Cpu.TotalCycles;

        // The cassette: the motor and the write line come from the PPI, and the
        // read line goes back to it.
        if (Tape is not null)
        {
            Ppi.TapeChanged += () =>
            {
                if (Ppi.TapeMotorOn) Tape.WriteBit(Ppi.TapeOutput, Cpu.TotalCycles);
            };
        }

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
        Fdc?.Reset();

        // Not TotalCycles + a line: RunFrame advances the target before running,
        // so pre-adding here would make the first line twice as long.
        _nextLine = Cpu.TotalCycles;
        _lastAudioTState = Cpu.TotalCycles;
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort port) => _ports.In(port);
    public void WritePort(ushort port, byte value) => _ports.Out(port, value);

    public void Step() => Cpu.Step();

    /// <summary>Runs one frame, with the length the CRTC currently describes.</summary>
    public void RunFrame()
    {
        int cyclesPerLine = (Crtc.HorizontalTotal + 1) * CyclesPerCharacter;
        int scanlines = Crtc.ScanlinesPerFrame;

        // A part-programmed CRTC can describe a frame of no lines, or one far
        // longer than any display. Clamping keeps a mid-reprogramming frame from
        // either spinning forever or returning without running any code.
        cyclesPerLine = Math.Clamp(cyclesPerLine, CyclesPerCharacter, 4096);
        scanlines = Math.Clamp(scanlines, 1, 1024);

        int vsyncStart = Crtc.VSyncStartScanline;
        int vsyncEnd = vsyncStart + Crtc.VerticalSyncWidth;

        for (int line = 0; line < scanlines; line++)
        {
            // The cassette read line is sampled as the machine runs: the
            // firmware polls port B bit 7 and measures how long the level
            // holds, so a level that only updates once a frame carries no data
            // at all.
            if (Tape is not null && Ppi.TapeMotorOn) Ppi.TapeInput = !Tape.ReadBit(Cpu.TotalCycles);

            bool vsync = line >= vsyncStart && line < vsyncEnd;

            // The Gate Array resynchronises its interrupt counter two HSyncs
            // after VSync begins, so it needs the leading edge, not the level.
            if (vsync && !Ppi.VSync) GateArray.OnVSync();
            Ppi.VSync = vsync;

            // Carried rather than measured from the current cycle count, so the
            // overshoot of the last instruction on each line does not accumulate
            // into a drifting frame rate.
            _nextLine += (ulong)cyclesPerLine;

            if (Cpu.TotalCycles > _nextLine + (ulong)cyclesPerLine)
            {
                _nextLine = Cpu.TotalCycles + (ulong)cyclesPerLine;
            }

            while (Cpu.TotalCycles < _nextLine) Cpu.Step();

            GateArray.OnHSync();
        }

        Ppi.VSync = false;

        // The CRTC counts fields for its cursor blink rate.
        Crtc.AdvanceField();
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
