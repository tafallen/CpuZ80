using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.Zx80;

/// <summary>
/// ZX80 I/O port bus.
/// </summary>
internal sealed class Zx80PortBus : IPortBus
{
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?            _tape;

    public Zx80PortBus(SinclairKeyboardAdapter? keyboard, ITapeDevice? tape = null)
    {
        _keyboard = keyboard;
        _tape     = tape;
    }

    public byte In(ushort port)
    {
        byte result = _keyboard?.Read((byte)(port >> 8)) ?? 0xFF;

        // Bit 6: EAR input — 0 = pulse present, 1 = silence. Default high (no tape).
        if (_tape is not null)
        {
            bool pulse = !_tape.ReadBit(); // ReadBit() true = silence, false = pulse
            if (pulse)
                result &= 0xBF; // clear bit 6
            else
                result |= 0x40; // set bit 6
        }

        return result;
    }

    public void Out(ushort port, byte value)
    {
        _tape?.WriteBit((value & 0x08) != 0); // bit 3 = MIC output
    }
}
