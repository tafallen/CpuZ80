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
        _ports = new ZxSpectrumPortBus(kbAdapter, tape, _beeper, () => Cpu!.TotalCycles);
        _host  = new ZxSpectrumCpuHost();

        Cpu = new Cpu(bus, _ports, _host);
        _video = new ZxSpectrumVideo(Ram);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x3F; // Default font in ROM
        _frameCounter = 0;
        _beeper.Reset(0);
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort address) => _ports.In(address);
    public void WritePort(ushort address, byte value) => _ports.Out(address, value);

    public void Step() => Cpu.Step();

    public void RunFrame()
    {
        // Assert INT signal at the start of the frame.
        // On real hardware, the ULA holds this low for 32 T-states.
        Cpu.IntPin = true;

        ulong target = Cpu.TotalCycles + CyclesPerFrame;
        ulong releaseIntAt = Cpu.TotalCycles + 32;

        while (Cpu.TotalCycles < target)
        {
            if (Cpu.IntPin && Cpu.TotalCycles >= releaseIntAt)
            {
                Cpu.IntPin = false;
            }
            Step();
        }
        _frameCounter++;
    }

    public void RenderFrame(IVideoSink sink)
    {
        // 1. Video
        // Flash toggles every 16 frames (approx 0.32 seconds)
        bool flashInverted = (_frameCounter & 0x10) != 0;
        _video.Render(sink, _ports.BorderColor, flashInverted);

        // 2. Audio
        if (_audioSink is not null)
        {
            _beeper.Render(_audioSink, Cpu.TotalCycles);
        }
    }
}
