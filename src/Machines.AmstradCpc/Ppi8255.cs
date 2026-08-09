using CpuZ80.Core;
using Machines.ZxSpectrum128;

namespace Machines.AmstradCpc;

/// <summary>
/// Intel 8255 PPI — the bridge between the CPU, the keyboard and the PSG.
/// </summary>
/// <remarks>
/// The CPC has no keyboard port. Reading a key means writing a row number to
/// port C, putting the PSG into read mode through port C's high bits, and
/// reading port A. Three components must all be right before one keypress
/// registers, and a failure in any of them looks identical from outside.
///
/// Port B bit 0 returns VSync, which is how the firmware synchronises. Getting
/// it wrong stalls the OS in a way that looks like a CPU bug.
///
/// Selected when A11 = 0, with A9/A8 choosing the port: 0 = A, 1 = B, 2 = C,
/// 3 = control.
/// </remarks>
public sealed class Ppi8255 : IPortBus
{
    private readonly Ay38912 _psg;
    private readonly ICpcKeyboard _keyboard;

    private byte _portA;
    private byte _portC;
    private byte _control = 0x82;   // A input, B input, C output — the CPC's usual setup

    /// <summary>Set by the machine each frame so the firmware can see the flyback.</summary>
    public bool VSync { get; set; }

    /// <summary>
    /// Refresh rate link: set for 50 Hz. Reported in port B bit 4.
    /// </summary>
    public bool Is50Hz { get; set; } = true;

    /// <summary>Manufacturer ID in port B bits 3-1. 7 = Amstrad.</summary>
    public int ManufacturerId { get; set; } = 7;

    /// <summary>Tape input, port B bit 7.</summary>
    public bool TapeInput { get; set; }

    public Ppi8255(Ay38912 psg, ICpcKeyboard keyboard)
    {
        _psg = psg;
        _keyboard = keyboard;
    }

    public void Reset()
    {
        _portA = 0;
        _portC = 0;
        _control = 0x82;
    }

    /// <summary>The keyboard row currently selected by port C's low nibble.</summary>
    public int SelectedKeyboardRow => _portC & 0x0F;

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
            2 => _portC,
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
                UpdatePsg();
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
            return;
        }

        int bit = (value >> 1) & 0x07;
        if ((value & 0x01) != 0) _portC |= (byte)(1 << bit);
        else _portC &= (byte)~(1 << bit);

        UpdatePsg();
    }

    private byte ReadPortA()
    {
        // Port A is wired to the PSG's data bus. With the PSG told to read, the
        // value it returns is whatever its selected register holds — and for
        // register 14 that is the keyboard matrix.
        int function = (_portC >> 6) & 0x03;

        if (function == 0x01)   // BDIR=0 BC1=1: read register
        {
            return _psg.SelectedRegister == 14
                ? _keyboard.ReadRow(SelectedKeyboardRow)
                : _psg.In(0xF400);
        }

        return _portA;
    }

    private byte ReadPortB()
    {
        byte value = 0;

        if (VSync) value |= 0x01;
        value |= (byte)((ManufacturerId & 0x07) << 1);
        if (Is50Hz) value |= 0x10;
        if (TapeInput) value |= 0x80;

        return value;
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
/// through the PSG rather than from a port, and because rows 9 carries the
/// joystick.
/// </remarks>
public interface ICpcKeyboard
{
    /// <summary>Returns the eight keys of a row, with a clear bit meaning pressed.</summary>
    byte ReadRow(int row);
}
