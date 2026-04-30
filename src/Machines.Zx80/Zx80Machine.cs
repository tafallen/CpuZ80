using CpuZ80.Core;
using Machines.Common;

namespace Machines.Zx80;

/// <summary>
/// Sinclair ZX80 machine compositor.
///
/// Address map (A14-based partial decode — primary ranges only):
///   0x0000–0x0FFF  ROM  (4K — BASIC/OS ROM image)
///   0x4000–0x43FF  RAM  (1K — system variables + display file + BASIC program)
///   Everything else → 0xFF (unmapped)
///
/// Emulator loop:
///   machine.Reset();
///   while (running)
///   {
///       host.PollEvents();
///       machine.RunFrame();   // steps CPU for one frame worth of T-states
///   }
/// </summary>
public sealed class Zx80Machine
{
    private const int    RomSize        = 0x1000; // 4K
    private const int    RamSize        = 0x0400; // 1K
    private const ulong  CyclesPerFrame = 64167;  // 3,250,000 Hz ÷ 50 Hz

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly Zx80PortBus _ports;

    /// <param name="rom">4K ROM image (ZX80 BASIC ROM). Must be exactly 4096 bytes.</param>
    /// <param name="keyboard">Physical keyboard source. Pass null for headless/test use.</param>
    public Zx80Machine(byte[] rom, IPhysicalKeyboard? keyboard = null)
    {
        if (rom.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {rom.Length}.", nameof(rom));

        Ram = new Ram(RamSize);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x0FFF, new Rom(rom));
        bus.Map(0x4000, 0x43FF, Ram);

        var kbAdapter = keyboard is not null ? new Zx80KeyboardAdapter(keyboard) : null;
        _ports = new Zx80PortBus(kbAdapter);

        Cpu = new Cpu(bus, _ports);
    }

    /// <summary>Read a hardware port — delegates to the port bus. Useful for testing.</summary>
    public byte ReadPort(ushort port) => _ports.In(port);

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
}
