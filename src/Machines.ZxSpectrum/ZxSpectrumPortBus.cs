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
    private readonly Func<ulong>?             _getCycles;

    public byte BorderColor { get; private set; }
    public bool SpeakerState { get; private set; }
    public bool MicState { get; private set; }

    public ZxSpectrumPortBus(SinclairKeyboardAdapter? keyboard, ITapeDevice? tape = null, BeeperDevice? beeper = null, Func<ulong>? getCycles = null)
    {
        _keyboard  = keyboard;
        _tape      = tape;
        _beeper    = beeper;
        _getCycles = getCycles;
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
            BorderColor  = (byte)(value & BorderMask);
            MicState     = (value & MIC_Bit) != 0;
            SpeakerState = (value & Speaker_Bit) != 0;
            
            if (_beeper is not null && _getCycles is not null)
            {
                // Mix bit 3 and 4 for audio output
                _beeper.SetLevel(_getCycles(), MicState || SpeakerState);
            }

            if (_tape is not null)
            {
                _tape.WriteBit(MicState);
            }
        }
    }
}
