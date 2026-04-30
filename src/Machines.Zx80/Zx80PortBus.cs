using CpuZ80.Core;

namespace Machines.Zx80;

/// <summary>
/// ZX80 I/O port bus.
///
/// IN  port: high byte selects keyboard half-row(s); returns active-low key state.
/// OUT port: reserved for tape MIC output (US-204).
/// </summary>
internal sealed class Zx80PortBus : IPortBus
{
    private readonly Zx80KeyboardAdapter? _keyboard;

    public Zx80PortBus(Zx80KeyboardAdapter? keyboard) => _keyboard = keyboard;

    public byte In(ushort port)
    {
        byte highByte = (byte)(port >> 8);
        return _keyboard?.Read(highByte) ?? 0xFF;
    }

    public void Out(ushort port, byte value)
    {
        // US-204: tape MIC output (port 0xFE bit 3) handled here.
    }
}
