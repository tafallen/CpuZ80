using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.Zx80;

/// <summary>
/// Represents the Ferranti 2C158E Uncommitted Logic Array (ULA) chip used in the ZX80.
/// Handles I/O port decoding and tape bitstreaming.
/// </summary>
public sealed class FerrantiUla2C158E : IPortBus, ICpuHost
{
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?            _tape;

    public FerrantiUla2C158E(SinclairKeyboardAdapter? keyboard = null, ITapeDevice? tape = null)
    {
        _keyboard = keyboard;
        _tape     = tape;
    }

    public void OnPortAccess(ushort address, Cpu cpu) { }
    public void OnMemoryAccess(ushort address, Cpu cpu) { }

    public byte In(ushort port)
    {
        byte result = _keyboard?.Read(port) ?? 0xFF;

        if (_tape is not null)
        {
            bool pulse = !_tape.ReadBit();
            if (pulse) result &= 0xBF;
            else result |= 0x40;
        }

        return result;
    }

    public void Out(ushort port, byte value)
    {
        _tape?.WriteBit((value & 0x08) != 0);
    }
}
