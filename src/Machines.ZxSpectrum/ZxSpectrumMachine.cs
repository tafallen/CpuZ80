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

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly ZxSpectrumPortBus _ports;
    private readonly ZxSpectrumCpuHost _host;
    private readonly ZxSpectrumVideo   _video;
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

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;
        _ports = new ZxSpectrumPortBus(kbAdapter, tape);
        _host  = new ZxSpectrumCpuHost();

        Cpu = new Cpu(bus, _ports, _host);
        _video = new ZxSpectrumVideo(Ram);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x3F; // Default font in ROM
        _frameCounter = 0;
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort address) => _ports.In(address);
    public void WritePort(ushort address, byte value) => _ports.Out(address, value);

    public void Step() => Cpu.Step();

    public void RenderFrame(IVideoSink sink)
    {
        // Flash toggles every 16 frames (approx 0.32 seconds)
        bool flashInverted = (_frameCounter & 0x10) != 0;
        _video.Render(sink, _ports.BorderColor, flashInverted);
        _frameCounter++;
    }
}
