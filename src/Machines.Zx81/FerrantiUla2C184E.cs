using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.Zx81;

/// <summary>
/// Represents the Ferranti 2C184E Uncommitted Logic Array (ULA) chip used in the ZX81.
/// Handles SLOW/FAST mode (NMI generator) and I/O port decoding.
/// </summary>
public sealed class FerrantiUla2C184E : IPortBus, ICpuHost
{
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?            _tape;

    public bool NmiEnabled { get; private set; }

    public FerrantiUla2C184E(SinclairKeyboardAdapter? keyboard = null, ITapeDevice? tape = null)
    {
        _keyboard = keyboard;
        _tape     = tape;
    }

    public void OnPortAccess(ushort address, Cpu cpu)
    {
        byte low = (byte)(address & 0xFF);
        if (low == 0xFD) NmiEnabled = false; 
        if (low == 0xFE) NmiEnabled = true;
    }

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
