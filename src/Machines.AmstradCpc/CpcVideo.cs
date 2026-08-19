using CpuZ80.Core;
using Machines.Common;

namespace Machines.AmstradCpc;

/// <summary>
/// Turns CRTC addresses and Gate Array pens into pixels.
/// </summary>
/// <remarks>
/// Geometry comes from the CRTC rather than being hardcoded, so software that
/// reprograms it for scrolling or overscan renders correctly. See
/// docs/amstrad-cpc.md §4 and §10.
/// </remarks>
public sealed class CpcVideo
{
    /// <summary>
    /// Frame width: enough for the standard 640-pixel display with a border
    /// either side.
    /// </summary>
    /// <remarks>
    /// The canvas is in mode 2 units — the highest resolution — so every mode
    /// scales up to it by a whole number and a mode change does not resize the
    /// window. Sizing it for mode 1 instead clips half the display, which looks
    /// like a rendering bug rather than a geometry one.
    /// </remarks>
    public const int Width = 768;

    /// <summary>
    /// Frame height: enough for the standard 200 scanlines with every line
    /// doubled, plus a border.
    /// </summary>
    /// <remarks>
    /// The horizontal axis is in mode 2 units, so a mode 1 pixel is two wide.
    /// Emitting one output line per scanline would then make every character
    /// 16 x 8 and the display would come out horizontally stretched — the text
    /// is legible and correct, which is what makes it easy to accept. Doubling
    /// the lines keeps pixels square in every mode.
    /// </remarks>
    public const int Height = 544;

    private const int LineScale = 2;

    /// <summary>
    /// Canvas pixels one CRTC character occupies, in every mode.
    /// </summary>
    /// <remarks>
    /// A character is always two display bytes, and each mode's pixels-per-byte
    /// and scale multiply out to eight — so a mode change alters the pixel size
    /// but never the width of the picture. That invariant is what lets one
    /// canvas serve all four modes.
    /// </remarks>
    private const int PixelsPerCharacter = 16;

    private readonly uint[] _frame = new uint[Width * Height];
    private readonly Mc6845 _crtc;
    private readonly AmstradGateArray _gateArray;
    private readonly CpcMemory _memory;

    public CpcVideo(Mc6845 crtc, AmstradGateArray gateArray, CpcMemory memory)
    {
        _crtc = crtc;
        _gateArray = gateArray;
        _memory = memory;
    }

    /// <summary>
    /// Expands one display byte into pixels, most significant pixel first.
    /// </summary>
    /// <remarks>
    /// The bits of a pixel are <b>not adjacent</b>, and they run the opposite
    /// way to intuition. A mode 0 byte is laid out A0 B0 A2 B2 A1 B1 A3 B3 from
    /// bit 7 down, where A0 is pixel A's *least* significant bit — so pixel A
    /// comes from bits 7, 3, 5 and 1 in that order. Decoding contiguous groups,
    /// or assuming bit 7 is the most significant, produces a display that looks
    /// almost right, which makes it easy to ship broken.
    /// </remarks>
    public static int[] DecodePixels(byte value, int mode)
    {
        switch (mode)
        {
            case 0:
            {
                // Two pixels, four bits each, interleaved.
                int p0 = ((value & 0x80) >> 7) | ((value & 0x08) >> 2)
                       | ((value & 0x20) >> 3) | ((value & 0x02) << 2);
                int p1 = ((value & 0x40) >> 6) | ((value & 0x04) >> 1)
                       | ((value & 0x10) >> 2) | ((value & 0x01) << 3);
                return [p0, p1];
            }

            case 1:
            {
                // Four pixels, two bits each, laid out A0 B0 C0 D0 A1 B1 C1 D1
                // from bit 7 down. The high bit of the byte is the pen index's
                // LOW bit, which is the part that catches people out.
                var pixels = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    int bit0 = (value >> (7 - i)) & 1;
                    int bit1 = (value >> (3 - i)) & 1;
                    pixels[i] = (bit1 << 1) | bit0;
                }
                return pixels;
            }

            case 2:
            {
                // Eight pixels, one bit each — the only linear mode.
                var pixels = new int[8];
                for (int i = 0; i < 8; i++) pixels[i] = (value >> (7 - i)) & 1;
                return pixels;
            }

            default:
            {
                // Mode 3 is undocumented but not a guess: the layout is
                // A0 B0 x x A1 B1 x x, so each pixel keeps mode 0's bits 0 and
                // 1 — from byte bits 7 and 3 — and discards the other two.
                // Taking mode 0's *top* two bits instead would be equally
                // plausible and completely wrong.
                int[] mode0 = DecodePixels(value, 0);
                return [mode0[0] & 0x03, mode0[1] & 0x03];
            }
        }
    }

    /// <summary>How many screen pixels one display byte covers, in mode 1 units.</summary>
    private static int PixelsPerByte(int mode) => mode switch
    {
        0 => 2,
        1 => 4,
        2 => 8,
        _ => 2,
    };

    /// <summary>
    /// Reads a display byte through the CRTC's address mapping.
    /// </summary>
    /// <remarks>
    /// The screen is not a linear frame buffer. The CRTC supplies a 14-bit
    /// address per character; the top two bits of R12 pick a 16K page, and the
    /// scanline within a character row is added at bit 11 — which is what puts
    /// consecutive scanlines &amp;800 apart in the default setup rather than
    /// adjacent.
    /// </remarks>
    public static int DisplayAddress(int ma, int ra, int byteInChar) =>
          ((ma & 0x3000) << 2)      // MA13-12 become A15-14: which 16K page
        | ((ra & 0x07) << 11)       // RA2-0 become A13-11: the scanline in the row
        | ((ma & 0x03FF) << 1)      // MA9-0 become A10-1
        | (byteInChar & 1);         // two bytes per CRTC character

    private byte ReadDisplayByte(int ma, int ra, int byteInChar)
    {
        int address = DisplayAddress(ma, ra, byteInChar);

        // Video fetches see the RAM chips directly: the Gate Array does not go
        // through the CPU's map, so a paged-in ROM is invisible to the display.
        // The display always reads the base 64K.
        int bank = (address >> 14) & 0x03;
        return _memory.Banks[bank].Read((ushort)(address & 0x3FFF));
    }

    /// <summary>Renders a frame from the current CRTC and Gate Array state.</summary>
    public void Render(IVideoSink sink)
    {
        uint border = CpcPalette.ToRgba(_gateArray.BorderColour);
        Array.Fill(_frame, border);

        int mode = _gateArray.ScreenMode;
        int pixelsPerByte = PixelsPerByte(mode);

        int charsWide = Math.Max(1, _crtc.HorizontalDisplayed);
        int rows = Math.Max(1, _crtc.VerticalDisplayed);
        int linesPerRow = _crtc.MaxScanline + 1;

        // The border is whatever the display does not cover, so its size comes
        // from the CRTC rather than being a constant. Hardcoding it puts any
        // non-standard geometry — overscan, a taller or narrower screen — in
        // the wrong place on the canvas while still looking plausible.
        int displayWidth = charsWide * PixelsPerCharacter;
        int displayHeight = rows * linesPerRow * LineScale;

        int originX = Math.Max(0, (Width - displayWidth) / 2);
        int originY = Math.Max(0, (Height - displayHeight) / 2);

        int start = _crtc.StartAddress;
        int y = originY;

        for (int row = 0; row < rows; row++)
        {
            for (int line = 0; line < linesPerRow; line++, y += LineScale)
            {
                if (y < 0 || y + LineScale > Height) continue;

                int x = originX;
                int rowAddress = start + row * charsWide;

                for (int ch = 0; ch < charsWide; ch++)
                {
                    // Each CRTC character is two bytes on the CPC.
                    for (int b = 0; b < 2; b++)
                    {
                        byte value = ReadDisplayByte(rowAddress + ch, line, b);
                        int[] pixels = DecodePixels(value, mode);

                        // Scale every mode to the same pixel width so a mode
                        // change does not resize the window.
                        int scale = 8 / pixelsPerByte;

                        foreach (int pen in pixels)
                        {
                            uint colour = CpcPalette.ToRgba(_gateArray.InkFor(pen));
                            for (int s = 0; s < scale; s++)
                            {
                                if (x >= 0 && x < Width)
                                {
                                    for (int dy = 0; dy < LineScale; dy++)
                                    {
                                        _frame[(y + dy) * Width + x] = colour;
                                    }
                                }
                                x++;
                            }
                        }
                    }
                }
            }
        }

        sink.SubmitFrame(_frame, Width, Height);
    }
}
