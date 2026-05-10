using CpuZ80.Core;
using Machines.Common;

namespace Machines.Zx80;

/// <summary>
/// Sinclair ZX80 machine compositor.
/// </summary>
public sealed class Zx80Machine
{
    private const int    RomSize        = 0x1000; // 4K
    private const int    RamSize        = 0x0400; // 1K
    private const ulong  CyclesPerFrame = 64167;  // 3,250,000 Hz ÷ 50 Hz

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly Zx80Video   _video;
    private readonly Zx80PortBus _ports;

    /// <param name="romImage">4K ROM image (ZX80 BASIC ROM). Must be exactly 4096 bytes.</param>
    /// <param name="keyboard">Physical keyboard source. Pass null for headless/test use.</param>
    /// <param name="tape">Tape device. Pass null for no tape.</param>
    public Zx80Machine(byte[] romImage, IPhysicalKeyboard? keyboard = null, ITapeDevice? tape = null)
    {
        if (romImage.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {romImage.Length}.", nameof(romImage));

        Ram = new Ram(RamSize);
        var rom = new Rom(romImage);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x0FFF, rom);
        bus.Map(0x4000, 0x43FF, Ram);

        var kbAdapter = keyboard is not null ? new Zx80KeyboardAdapter(keyboard) : null;
        _ports = new Zx80PortBus(kbAdapter, tape);

        Cpu = new Cpu(bus, _ports);
        _video = new Zx80Video(rom, Ram);
    }

    /// <summary>Read a hardware port — delegates to the port bus. Useful for testing.</summary>
    public byte ReadPort(ushort port) => _ports.In(port);

    /// <summary>Write a hardware port — delegates to the port bus. Useful for testing.</summary>
    public void WritePort(ushort port, byte value) => _ports.Out(port, value);

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x0E;
    }

    public void Step() => Cpu.Step();

    public void RunFrame()
    {
        ulong target = Cpu.TotalCycles + CyclesPerFrame;
        while (Cpu.TotalCycles < target)
            Cpu.Step();
    }

    /// <summary>
    /// Renders the current state of the display file to the video sink.
    /// </summary>
    public void RenderFrame(IVideoSink sink) => _video.Render(sink);
}
