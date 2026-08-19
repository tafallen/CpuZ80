using CpuZ80.Core;
using Machines.ZxSpectrum128;

namespace Machines.AmstradCpc;

/// <summary>
/// Intel 8255 PPI — on a CPC, the bridge between the CPU, the keyboard and the
/// PSG.
/// </summary>
/// <remarks>
/// The CPC has no keyboard port. Reading a key means writing a row number to
/// port C, putting the PSG into read mode through port C's high bits, and
/// reading port A — which the firmware first has to turn round into an input,
/// because it uses the same port to write to the PSG. The matrix itself hangs
/// off the PSG's I/O port rather than off this chip, so the PPI only supplies
/// the row.
///
/// Port B bit 0 returns VSync, which is how the firmware synchronises. Getting
/// it wrong stalls the OS in a way that looks like a CPU bug.
///
/// Selected when A11 = 0, with A9/A8 choosing the port: 0 = A, 1 = B, 2 = C,
/// 3 = control.
///
/// Modes 1 and 2 are implemented for completeness, but the CPC connects nothing
/// to the handshake lines: it uses mode 0 throughout.
/// </remarks>
public sealed class Ppi8255 : IPortBus
{
    private readonly Ay38912 _psg;

    private byte _portA;
    private byte _portB;
    private byte _portC;

    /// <summary>The mode-set word. A input, B input, C output is the CPC's usual setup.</summary>
    private byte _control = 0x82;

    /// <summary>Set by the machine each frame so the firmware can see the flyback.</summary>
    public bool VSync { get; set; }

    /// <summary>Refresh rate link: set for 50 Hz. Reported in port B bit 4.</summary>
    public bool Is50Hz { get; set; } = true;

    /// <summary>Manufacturer ID in port B bits 3-1. 7 = Amstrad.</summary>
    public int ManufacturerId { get; set; } = 7;

    /// <summary>Tape input, port B bit 7.</summary>
    public bool TapeInput { get; set; }

    /// <summary>
    /// What an input half of port C reads when nothing drives it. Nothing does
    /// on a CPC, where port C is an output throughout.
    /// </summary>
    public byte PortCInput { get; set; } = 0xFF;

    public Ppi8255(Ay38912 psg) => _psg = psg;

    public void Reset()
    {
        // A mode-set word resets the output latches, and reset is a mode set.
        _portA = 0;
        _portB = 0;
        _portC = 0;
        _control = 0x82;
    }

    // ── The control word ─────────────────────────────────────────────────────

    /// <summary>Group A mode: 0, 1 or 2. Bits 6-5, where 1x is mode 2.</summary>
    public int GroupAMode => ((_control >> 5) & 0x03) switch { 0 => 0, 1 => 1, _ => 2 };

    /// <summary>Group B mode: 0 or 1, from bit 2.</summary>
    public int GroupBMode => (_control >> 2) & 0x01;

    /// <summary>Port A is an input when control bit 4 is set.</summary>
    public bool PortAIsInput => (_control & 0x10) != 0;

    /// <summary>Port B is an input when control bit 1 is set.</summary>
    public bool PortBIsInput => (_control & 0x02) != 0;

    /// <summary>Port C's upper nibble is an input when control bit 3 is set.</summary>
    public bool PortCUpperIsInput => (_control & 0x08) != 0;

    /// <summary>Port C's lower nibble is an input when control bit 0 is set.</summary>
    public bool PortCLowerIsInput => (_control & 0x01) != 0;

    /// <summary>The keyboard row currently selected by port C's low nibble.</summary>
    public int SelectedKeyboardRow => _portC & 0x0F;

    // ── Mode 1 and 2 handshaking ─────────────────────────────────────────────
    //
    // Port C carries the handshake lines when either group leaves mode 0:
    // group A input uses PC4 as STB, PC5 as IBF and PC3 as INTR; group A output
    // uses PC7 as OBF, PC6 as ACK and PC3 as INTR. Group B uses PC2, PC1 and
    // PC0. Mode 2 uses all five of PC3-PC7 at once.

    private const byte PortCObfA  = 0x80;
    private const byte PortCAckA  = 0x40;
    private const byte PortCIbfA  = 0x20;
    private const byte PortCStbA  = 0x10;
    private const byte PortCIntrA = 0x08;
    private const byte PortCStbB  = 0x04;
    private const byte PortCIbfB  = 0x02;
    private const byte PortCIntrB = 0x01;

    private byte _handshake;
    private byte _inputLatchA;
    private byte _inputLatchB;

    /// <summary>True while an interrupt is being requested by either group.</summary>
    public bool InterruptRequested => (_handshake & (PortCIntrA | PortCIntrB)) != 0;

    /// <summary>
    /// A peripheral strobing data into port A, in mode 1 input or mode 2.
    /// </summary>
    public void StrobePortA(byte value)
    {
        if (GroupAMode == 0) return;

        _inputLatchA = value;
        _handshake |= (byte)(PortCIbfA | PortCIntrA);
    }

    /// <summary>A peripheral acknowledging it has taken port A's output.</summary>
    public void AcknowledgePortA()
    {
        if (GroupAMode == 0) return;

        _handshake &= unchecked((byte)~PortCObfA);
        _handshake |= PortCIntrA;
    }

    /// <summary>A peripheral strobing data into port B, in mode 1 input.</summary>
    public void StrobePortB(byte value)
    {
        if (GroupBMode == 0) return;

        _inputLatchB = value;
        _handshake |= (byte)(PortCIbfB | PortCIntrB);
    }

    // ── Ports ────────────────────────────────────────────────────────────────

    private static bool IsPpiPort(ushort port) => (port & 0x0800) == 0;

    private static int PortIndex(ushort port) => (port >> 8) & 0x03;

    public byte In(ushort port)
    {
        if (!IsPpiPort(port)) return 0xFF;

        return PortIndex(port) switch
        {
            0 => ReadPortA(),
            1 => ReadPortB(),
            2 => ReadPortC(),
            _ => _control,
        };
    }

    public void Out(ushort port, byte value)
    {
        if (!IsPpiPort(port)) return;

        switch (PortIndex(port))
        {
            case 0:
                _portA = value;

                // Loading the output latch in mode 1 or 2 raises OBF and clears
                // the interrupt, which is what tells the peripheral to collect.
                if (GroupAMode != 0)
                {
                    _handshake |= PortCObfA;
                    _handshake &= unchecked((byte)~PortCIntrA);
                }

                UpdatePsg();
                break;

            case 1:
                _portB = value;
                break;

            case 2:
                _portC = value;
                UpdatePsg();
                break;

            case 3:
                WriteControl(value);
                break;
        }
    }

    private void WriteControl(byte value)
    {
        // Bit 7 set is a mode-set write; clear is bit set/reset on port C.
        if ((value & 0x80) != 0)
        {
            _control = value;

            // A mode set resets the output latches and every handshake line.
            _portA = 0;
            _portB = 0;
            _portC = 0;
            _handshake = 0;
            return;
        }

        int bit = (value >> 1) & 0x07;
        if ((value & 0x01) != 0) _portC |= (byte)(1 << bit);
        else _portC &= (byte)~(1 << bit);

        UpdatePsg();
    }

    private byte ReadPortA()
    {
        // An output port reads back its own latch, not the outside world. The
        // CPC firmware turns port A round before reading the keyboard, because
        // it uses the same port to write to the PSG.
        if (!PortAIsInput) return _portA;

        if (GroupAMode != 0)
        {
            // Reading clears IBF and the interrupt: the buffer is now empty.
            _handshake &= unchecked((byte)~(PortCIbfA | PortCIntrA));
            return _inputLatchA;
        }

        // Port A is wired to the PSG's data bus. With the PSG told to read, the
        // value it returns is whatever its selected register holds — and the
        // keyboard hangs off the PSG's own I/O port, not off the PPI, so there
        // is nothing to special-case here.
        int function = (_portC >> 6) & 0x03;

        if (function == 0x01) return _psg.In(0xFFFD);   // BDIR=0 BC1=1: read register

        return _portA;
    }

    private byte ReadPortB()
    {
        if (!PortBIsInput) return _portB;

        if (GroupBMode != 0)
        {
            _handshake &= unchecked((byte)~(PortCIbfB | PortCIntrB));
            return _inputLatchB;
        }

        byte value = 0;

        if (VSync) value |= 0x01;
        value |= (byte)((ManufacturerId & 0x07) << 1);
        if (Is50Hz) value |= 0x10;
        if (TapeInput) value |= 0x80;

        return value;
    }

    private byte ReadPortC()
    {
        // Each half reads its latch when it is an output and the outside world
        // when it is an input, so the two halves can differ.
        byte upper = PortCUpperIsInput ? (byte)(PortCInput & 0xF0) : (byte)(_portC & 0xF0);
        byte lower = PortCLowerIsInput ? (byte)(PortCInput & 0x0F) : (byte)(_portC & 0x0F);

        byte value = (byte)(upper | lower);

        // Whichever handshake lines are in use override the latch, since the
        // chip drives them itself.
        byte mask = HandshakeMask();
        if (mask != 0) value = (byte)((value & ~mask) | (_handshake & mask));

        return value;
    }

    /// <summary>Which port C bits the chip is driving itself, given the modes.</summary>
    private byte HandshakeMask()
    {
        byte mask = 0;

        if (GroupAMode == 2)
        {
            mask |= PortCObfA | PortCAckA | PortCIbfA | PortCStbA | PortCIntrA;
        }
        else if (GroupAMode == 1)
        {
            mask |= PortAIsInput
                ? (byte)(PortCStbA | PortCIbfA | PortCIntrA)
                : (byte)(PortCObfA | PortCAckA | PortCIntrA);
        }

        if (GroupBMode == 1) mask |= PortCStbB | PortCIbfB | PortCIntrB;

        return mask;
    }

    /// <summary>
    /// Drives the PSG from port C's two high bits, which carry BDIR and BC1.
    /// </summary>
    private void UpdatePsg()
    {
        switch ((_portC >> 6) & 0x03)
        {
            case 0x02:   // BDIR=1 BC1=0: write the selected register
                _psg.Out(0xBFFD, _portA);
                break;

            case 0x03:   // BDIR=1 BC1=1: select a register
                _psg.Out(0xFFFD, _portA);
                break;
        }
    }
}

/// <summary>
/// The CPC keyboard matrix: ten rows of eight keys, active low.
/// </summary>
/// <remarks>
/// Separate from <c>IPhysicalKeyboard</c> because the CPC's matrix is read
/// through the PSG rather than from a port, and because row 9 carries the
/// joystick.
/// </remarks>
public interface ICpcKeyboard
{
    /// <summary>Returns the eight keys of a row, with a clear bit meaning pressed.</summary>
    byte ReadRow(int row);
}
