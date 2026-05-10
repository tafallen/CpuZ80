using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Handles ZX Spectrum I/O port mapping with authentic Floating Bus behavior.
/// </summary>
internal sealed class ZxSpectrumPortBus : IPortBus
{
    private const ushort UlaPortMask = 0x0001;
    
    private const byte BorderMask   = 0x07;
    private const byte MIC_Bit      = 0x08;
    private const byte Speaker_Bit  = 0x10;
    private const byte EAR_Bit      = 0x40;

    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?             _tape;
    private readonly BeeperDevice?            _beeper;
    private Cpu?                              _cpu;

    private readonly object _lock = new();
    private List<(ulong TState, byte Color)> _activeBorder = new(256);
    private List<(ulong TState, byte Color)> _renderBorder = new(256);

    public byte BorderColor { get; private set; }
    public byte FloatingBusValue { get; set; } = 0xFF;

    public IReadOnlyList<(ulong TState, byte Color)> RenderBorderTransitions => _renderBorder;

    public ZxSpectrumPortBus(SinclairKeyboardAdapter? keyboard, ITapeDevice? tape = null, BeeperDevice? beeper = null)
    {
        _keyboard  = keyboard;
        _tape      = tape;
        _beeper    = beeper;
    }

    public void ConnectCpu(Cpu cpu) => _cpu = cpu;

    public void CommitTransitions()
    {
        lock (_lock)
        {
            var temp = _renderBorder;
            _renderBorder = _activeBorder;
            _activeBorder = temp;
            _activeBorder.Clear();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            BorderColor = 0;
            _activeBorder.Clear();
            _renderBorder.Clear();
        }
    }

    public byte In(ushort port)
    {
        // 1. ULA responds to even port addresses
        if ((port & UlaPortMask) == 0)
        {
            byte result = _keyboard?.Read((byte)(port >> 8)) ?? 0xFF;

            if (_tape is not null)
            {
                if (!_tape.ReadBit()) 
                    result &= unchecked((byte)~EAR_Bit); 
                else
                    result |= EAR_Bit;
            }
            
            return result;
        }

        // 2. Floating Bus: unmapped ports return the ULA's current attribute fetch
        return FloatingBusValue;
    }

    public void Out(ushort port, byte value)
    {
        if ((port & UlaPortMask) == 0)
        {
            byte newColor = (byte)(value & BorderMask);
            if (newColor != BorderColor)
            {
                BorderColor = newColor;
                if (_cpu is not null)
                {
                    lock (_lock) _activeBorder.Add((_cpu.TotalCycles, newColor));
                }
            }

            bool mic     = (value & MIC_Bit) != 0;
            bool speaker = (value & Speaker_Bit) != 0;
            
            if (_beeper is not null && _cpu is not null)
            {
                int level = (speaker ? 9 : 0) + (mic ? 1 : 0);
                _beeper.SetLevel(_cpu.TotalCycles, level);
            }

            _tape?.WriteBit(mic);
        }
    }
}
