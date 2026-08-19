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

    /// <summary>R0: total characters per line, minus one. This sets the line period.</summary>
    public int HorizontalTotal => _registers[0];

    /// <summary>R2: the character position at which HSync starts.</summary>
    public int HorizontalSyncPosition => _registers[2];

    /// <summary>R3 low nibble: HSync width in characters. Zero means no HSync.</summary>
    public int HorizontalSyncWidth => _registers[3] & 0x0F;

    /// <summary>R3 high nibble: VSync width in scanlines. Zero means 16.</summary>
    public int VerticalSyncWidth
    {
        get
        {
            int width = (_registers[3] >> 4) & 0x0F;
            return width == 0 ? 16 : width;
        }
    }

    /// <summary>R5: extra scanlines added to the frame after the last character row.</summary>
    public int VerticalTotalAdjust => _registers[5];

    /// <summary>R7: the character row at which VSync starts.</summary>
    public int VerticalSyncPosition => _registers[7];

    /// <summary>Scanlines in a whole frame, from R4, R9 and R5.</summary>
    /// <remarks>
    /// This is what makes the frame rate the CRTC's business rather than a
    /// constant: reprogramming any of the three changes how long a frame takes,
    /// and raster effects depend on that being followed.
    /// </remarks>
    public int ScanlinesPerFrame => (VerticalTotal + 1) * (MaxScanline + 1) + VerticalTotalAdjust;

    /// <summary>The scanline within the frame at which VSync begins.</summary>
    public int VSyncStartScanline => VerticalSyncPosition * (MaxScanline + 1);

    /// <summary>R8: interlace mode. 0 and 2 are non-interlaced, 1 is sync, 3 is sync and video.</summary>
    /// <remarks>
    /// Stored and reported, but it does not change what this machine draws: the
    /// CPC's display path does not use the CRTC's interlace output, so a program
    /// that sets it sees no difference on real hardware either.
    /// </remarks>
    public int InterlaceMode => _registers[8] & 0x03;

    /// <summary>R14/R15: where the cursor sits in the refresh address space.</summary>
    public int CursorAddress => ((_registers[14] & 0x3F) << 8) | _registers[15];

    /// <summary>R10 bits 0-4: the first scanline of a character row the cursor covers.</summary>
    public int CursorStartLine => _registers[10] & 0x1F;

    /// <summary>R11: the last scanline of a character row the cursor covers.</summary>
    public int CursorEndLine => _registers[11] & 0x1F;

    /// <summary>R10 bits 6-5: 0 steady, 1 off, 2 blink every 16 fields, 3 every 32.</summary>
    public int CursorBlinkMode => (_registers[10] >> 5) & 0x03;

    /// <summary>R16/R17: the address latched the last time the light pen was strobed.</summary>
    public int LightPenAddress => ((_registers[16] & 0x3F) << 8) | _registers[17];

    /// <summary>Fields elapsed, which is what the cursor blink rate counts.</summary>
    private int _fields;

    /// <summary>
    /// True when the cursor is currently in its visible half of the blink cycle.
    /// </summary>
    public bool CursorBlinkOn => CursorBlinkMode switch
    {
        0 => true,                          // steady
        1 => false,                         // disabled
        2 => (_fields & 0x08) != 0,         // 16-field period
        _ => (_fields & 0x10) != 0,         // 32-field period
    };

    /// <summary>
    /// True when the cursor output would be active for this address and raster
    /// line.
    /// </summary>
    /// <remarks>
    /// The CPC leaves the CRTC's cursor pin unconnected and draws its own cursor
    /// in software, so this changes nothing on screen here. It is the chip's
    /// behaviour rather than the machine's, and a different host could use it.
    /// </remarks>
    public bool IsCursorAt(int ma, int ra)
    {
        if (!CursorBlinkOn) return false;
        if ((ma & 0x3FFF) != CursorAddress) return false;

        // A start line above the end line disables the cursor rather than
        // wrapping, which is what the hardware does.
        return ra >= CursorStartLine && ra <= CursorEndLine;
    }

    /// <summary>Advances the blink counter. One call per field.</summary>
    public void AdvanceField() => _fields++;

    /// <summary>
    /// Latches the current refresh address into R16/R17, as the light pen strobe
    /// does.
    /// </summary>
    public void StrobeLightPen(int address)
    {
        _registers[16] = (byte)((address >> 8) & 0x3F);
        _registers[17] = (byte)(address & 0xFF);
    }

    public void Reset()
    {
        Array.Clear(_registers);
        _selected = 0;
        _fields = 0;

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
            // A standard MC6845 has no status register. Returning zero is the
            // part's behaviour, not a gap: the types that do have one are the
            // UM6845R and the ASICs, and the CPC firmware reads VSync from the
            // PPI rather than from here.
            2 => 0x00,
            3 => ReadRegister(),
            _ => 0xFF,
        };
    }

    /// <summary>
    /// Only R14-R17 read back on a standard MC6845: the cursor address is
    /// read/write and the light pen is read-only. Everything else is write-only
    /// and reads as zero.
    /// </summary>
    /// <remarks>
    /// The later MC6845*1 and the UM6845R fitted to some CPCs also return R12
    /// and R13, and some of those parts have a status register this one does
    /// not. Programs that detect the CRTC type do it by reading exactly these
    /// registers, so widening the readback here would make this chip claim to be
    /// a different one.
    /// </remarks>
    private byte ReadRegister()
    {
        if (_selected is >= 14 and < RegisterCount) return _registers[_selected];
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
        8 => (byte)(value & 0x03),
        14 => (byte)(value & 0x3F),

        // R10 is seven bits, not five: bits 0-4 are the cursor's first scanline
        // and bits 6-5 are its blink mode. Masking it to five silently discards
        // the blink setting, so every cursor comes out steady.
        10 => (byte)(value & 0x7F),

        5 or 9 or 11 => (byte)(value & 0x1F),
        12 => (byte)(value & 0x3F),
        _ => value,
    };
}
