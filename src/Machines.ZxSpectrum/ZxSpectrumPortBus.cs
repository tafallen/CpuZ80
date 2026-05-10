using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Handles ZX Spectrum I/O port mapping.
/// </summary>
internal sealed class ZxSpectrumPortBus : IPortBus
{
    // The ULA responds to any even port address (A0 low)
    private const ushort UlaPortMask = 0x0001;
    
    // ULA bits (Port 0xFE)
    private const byte BorderMask   = 0x07; // Bits 0-2: Border color
    private const byte MIC_Bit      = 0x08; // Bit 3: MIC output (Tape)
    private const byte Speaker_Bit  = 0x10; // Bit 4: Beeper speaker
    private const byte EAR_Bit      = 0x40; // Bit 6: EAR input (Tape)

    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?             _tape;
    private readonly BeeperDevice?            _beeper;
    private Cpu?                              _cpu;

    public byte BorderColor { get; private set; }
    public bool SpeakerState { get; private set; }
    public bool MicState { get; private set; }

    private readonly List<(ulong TState, byte Color)> _borderTransitions = new(256);
    public IReadOnlyList<(ulong TState, byte Color)> BorderTransitions => _borderTransitions;

    public ZxSpectrumPortBus(SinclairKeyboardAdapter? keyboard, ITapeDevice? tape = null, BeeperDevice? beeper = null)
    {
        _keyboard  = keyboard;
        _tape      = tape;
        _beeper    = beeper;
    }

    public void ConnectCpu(Cpu cpu) => _cpu = cpu;

    public void ClearTransitions()
    {
        _borderTransitions.Clear();
    }

    public byte In(ushort port)
    {
        // ULA responds to even port addresses
        if ((port & UlaPortMask) == 0)
        {
            // Keyboard half-row selection via address lines A8-A15
            byte result = _keyboard?.Read((byte)(port >> 8)) ?? 0xFF;

            // EAR bit (Tape Input)
            if (_tape is not null)
            {
                if (!_tape.ReadBit()) 
                    result &= unchecked((byte)~EAR_Bit); 
                else
                    result |= EAR_Bit;
            }
            
            return result;
        }

        return 0xFF;
    }

    public void Out(ushort port, byte value)
    {
        if ((port & UlaPortMask) == 0)
        {
            byte newColor = (byte)(value & BorderMask);
            if (newColor != BorderColor)
            {
                BorderColor = newColor;
                if (_cpu is not null) _borderTransitions.Add((_cpu.TotalCycles, newColor));
            }

            MicState     = (value & MIC_Bit) != 0;
            SpeakerState = (value & Speaker_Bit) != 0;
            
            if (_beeper is not null && _cpu is not null)
            {
                // Mix bit 3 and 4 for audio output.
                // On real hardware, Speaker (bit 4) is significantly louder.
                // We use a relative scale of 9:1.
                int level = (SpeakerState ? 9 : 0) + (MicState ? 1 : 0);
                _beeper.SetLevel(_cpu.TotalCycles, level);
            }

            if (_tape is not null)
            {
                _tape.WriteBit(MicState);
            }
        }
    }
}
