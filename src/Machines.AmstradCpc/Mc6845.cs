using CpuZ80.Core;

namespace Machines.AmstradCpc;

/// <summary>
/// Motorola 6845 CRT controller.
/// </summary>
/// <remarks>
/// The CPC's screen geometry is programmable, which is why the video circuit
/// cannot be the fixed raster loop the Sinclair machines use. Software changes
/// these registers for hardware scrolling, overscan and split screens, and the
/// renderer has to follow.
///
/// Selected when A14 = 0 and A13 = 1 (&amp;BCxx-&amp;BFxx), with A9 and A8
/// choosing the function: 0 select, 1 write, 2 status, 3 read.
/// </remarks>
public sealed class Mc6845 : IPortBus
{
    public const int RegisterCount = 18;

    private readonly byte[] _registers = new byte[RegisterCount];
    private int _selected;

    /// <summary>Registers 0-15 read back; 16 and 17 are the light pen and are read-only.</summary>
    private static readonly bool[] Writable =
    [
        true, true, true, true, true, true, true, true,
        true, true, true, true, true, true, true, true,
        false, false,
    ];

    public byte this[int register] => _registers[register % RegisterCount];

    // Named accessors for the registers the video path actually needs.

    /// <summary>R1: displayed characters per line.</summary>
    public int HorizontalDisplayed => _registers[1];

    /// <summary>R6: displayed character rows.</summary>
    public int VerticalDisplayed => _registers[6];

    /// <summary>R9: scanlines per character row, minus one.</summary>
    public int MaxScanline => _registers[9];

    /// <summary>R12/R13: the display start address, 14 bits.</summary>
    public int StartAddress => ((_registers[12] & 0x3F) << 8) | _registers[13];

    /// <summary>R4: total character rows per frame, minus one.</summary>
    public int VerticalTotal => _registers[4];

    /// <summary>R0: total characters per line, minus one.</summary>
    public int HorizontalTotal => _registers[0];

    public void Reset()
    {
        Array.Clear(_registers);
        _selected = 0;

        // The firmware programs these itself, but a machine that is inspected
        // before it runs should describe a plausible screen rather than a
        // zero-sized one.
        _registers[0] = 63;
        _registers[1] = 40;
        _registers[4] = 38;
        _registers[6] = 25;
        _registers[9] = 7;
    }

    public Mc6845() => Reset();

    // ── Ports ────────────────────────────────────────────────────────────────

    /// <summary>CRTC: A14 clear, A13 set.</summary>
    private static bool IsCrtcPort(ushort port) => (port & 0x6000) == 0x2000;

    /// <summary>A9 and A8 select the function within the CRTC.</summary>
    private static int Function(ushort port) => (port >> 8) & 0x03;

    public byte In(ushort port)
    {
        if (!IsCrtcPort(port)) return 0xFF;

        return Function(port) switch
        {
            2 => 0x00,                       // status
            3 => ReadRegister(),
            _ => 0xFF,
        };
    }

    private byte ReadRegister()
    {
        // Only registers 12-17 read back on a 6845; the rest return 0.
        if (_selected is >= 12 and < RegisterCount) return _registers[_selected];
        return 0x00;
    }

    public void Out(ushort port, byte value)
    {
        if (!IsCrtcPort(port)) return;

        switch (Function(port))
        {
            case 0:
                _selected = value & 0x1F;
                break;

            case 1:
                if (_selected < RegisterCount && Writable[_selected])
                {
                    _registers[_selected] = MaskFor(_selected, value);
                }
                break;
        }
    }

    /// <summary>Several registers have fewer than 8 meaningful bits.</summary>
    private static byte MaskFor(int register, byte value) => register switch
    {
        4 or 6 or 7 => (byte)(value & 0x7F),
        5 or 9 or 10 or 11 => (byte)(value & 0x1F),
        12 => (byte)(value & 0x3F),
        _ => value,
    };
}
