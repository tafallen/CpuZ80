using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Sinclair ZX Spectrum 48K machine compositor.
/// </summary>
public sealed class ZxSpectrumMachine
{
    private const int RomSize = 0x4000; // 16K
    private const int RamSize = 0xC000; // 48K
    private const ulong CyclesPerFrame = 69888; // 3.5 MHz @ 50Hz

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly ZxSpectrumPortBus _ports;
    private readonly ZxSpectrumCpuHost _host;
    private readonly ZxSpectrumVideo   _video;
    private readonly BeeperDevice      _beeper;
    private readonly IAudioSink?       _audioSink;
    
    private int _frameCounter;
    private ulong _frameStartCycles;
    private ulong _renderFrameStartCycles;
    private ulong _nextFrameTarget;

    // Transition buffering to prevent race conditions during rendering
    private readonly List<(ulong TState, byte Color)> _renderBorderTransitions = new(256);

    public ZxSpectrumMachine(byte[] romImage, IPhysicalKeyboard? keyboard = null, IAudioSink? audio = null, ITapeDevice? tape = null)
    {
        if (romImage.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {romImage.Length}.", nameof(romImage));

        var rom = new Rom(romImage);
        Ram = new Ram(RamSize);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x3FFF, rom);
        bus.Map(0x4000, 0xFFFF, Ram);

        _beeper    = new BeeperDevice();
        _audioSink = audio;

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;
        _ports = new ZxSpectrumPortBus(kbAdapter, tape, _beeper);
        _host  = new ZxSpectrumCpuHost();

        Cpu = new Cpu(bus, _ports, _host);
        _ports.ConnectCpu(Cpu);
        _video = new ZxSpectrumVideo(Ram);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x3F; 
        _frameCounter = 0;
        _frameStartCycles = 0;
        _renderFrameStartCycles = 0;
        _nextFrameTarget = 0;
        _beeper.Reset(0);
        _ports.Reset();
        _renderBorderTransitions.Clear();
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);
    public byte ReadPort(ushort address) => _ports.In(address);
    public void WritePort(ushort address, byte value)
    {
        _host.OnPortAccess(address, Cpu);
        _ports.Out(address, value);
    }

    public void Step() => Cpu.Step();

    public void RunFrame()
    {
        _frameStartCycles = Cpu.TotalCycles;
        _host.FrameStartCycles = _frameStartCycles;

        // Start of frame: Commit and clear transitions
        _ports.CommitTransitions();
        _beeper.CommitTransitions();
        
        // Snapshot the cycle anchor for rendering
        _renderFrameStartCycles = _frameStartCycles;

        // Assert 50Hz INT
        Cpu.IntPin = true;
        ulong releaseIntAt = Cpu.TotalCycles + 32;

        _nextFrameTarget += CyclesPerFrame;
        
        // Safety: if we fall too far behind, reset the target
        if (Cpu.TotalCycles > _nextFrameTarget + CyclesPerFrame)
            _nextFrameTarget = Cpu.TotalCycles + CyclesPerFrame;

        while (Cpu.TotalCycles < _nextFrameTarget)
        {
            if (Cpu.IntPin && Cpu.TotalCycles >= releaseIntAt)
                Cpu.IntPin = false;

            Step();
            
            // Sync floating bus value
            _ports.FloatingBusValue = _host.CurrentFloatingBusValue;
        }
        _frameCounter++;
    }

    public void RenderFrame(IVideoSink sink)
    {
        bool flashInverted = (_frameCounter & 0x10) != 0;

        // Video rendering uses the snapshotted border transitions and frame anchor
        _video.Render(sink, _ports.RenderBorderTransitions, _ports.BorderColor, flashInverted, _renderFrameStartCycles);

        // Audio rendering uses the snapshotted transitions
        if (_audioSink is not null)
        {
            _beeper.Render(_audioSink, Cpu.TotalCycles);
        }
    }

    /// <summary>
    /// Loads a .SNA file snapshot (48K version).
    /// </summary>
    public void LoadSnapshot(Stream data)
    {
        byte[] header = new byte[27];
        if (data.Read(header, 0, 27) != 27) throw new InvalidDataException("Invalid .SNA header");

        Cpu.I = header[0];
        Cpu.HL_ = (ushort)(header[1] | (header[2] << 8));
        Cpu.DE_ = (ushort)(header[3] | (header[4] << 8));
        Cpu.BC_ = (ushort)(header[5] | (header[6] << 8));
        Cpu.AF_ = (ushort)(header[7] | (header[8] << 8));
        Cpu.HL = (ushort)(header[9] | (header[10] << 8));
        Cpu.DE = (ushort)(header[11] | (header[12] << 8));
        Cpu.BC = (ushort)(header[13] | (header[14] << 8));
        Cpu.IY = (ushort)(header[15] | (header[16] << 8));
        Cpu.IX = (ushort)(header[17] | (header[18] << 8));

        // Byte 19: Bit 2 is IFF2
        bool iff2 = (header[19] & 0x04) != 0;
        Cpu.IFF1 = iff2;
        Cpu.IFF2 = iff2;

        Cpu.R = header[20];
        Cpu.AF = (ushort)(header[21] | (header[22] << 8));
        Cpu.SP = (ushort)(header[23] | (header[24] << 8));
        Cpu.IM = header[25] & 0x03;
        _ports.Out(0x00FE, header[26]); // Set border color

        byte[] ram = new byte[49152];
        int read = data.Read(ram, 0, 49152);
        Ram.Load(0, ram.Take(read).ToArray());

        // For .SNA, the PC is stored on the stack and must be popped.
        byte lo = Cpu.ReadMemory(Cpu.SP++);
        byte hi = Cpu.ReadMemory(Cpu.SP++);
        Cpu.PC = (ushort)(lo | (hi << 8));
    }
}
