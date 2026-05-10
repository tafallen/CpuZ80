using CpuZ80.Core;
using Machines.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Handles ZX Spectrum 256x192 bitmapped display with color attributes and border.
/// Renders to a 320x240 buffer to include the standard Sinclair border.
/// </summary>
public sealed class ZxSpectrumVideo
{
    public const int TotalWidth = 320;
    public const int TotalHeight = 240;
    public const int ActiveWidth = 256;
    public const int ActiveHeight = 192;
    public const int BorderWidth = (TotalWidth - ActiveWidth) / 2;  // 32 pixels
    public const int BorderHeight = (TotalHeight - ActiveHeight) / 2; // 24 pixels

    private static readonly uint[] PaletteNormal =
    [
        0xFF000000, // 0: Black
        0xFF0000D7, // 1: Blue
        0xFFD70000, // 2: Red
        0xFFD700D7, // 3: Magenta
        0xFF00D700, // 4: Green
        0xFF00D7D7, // 5: Cyan
        0xFFD7D700, // 6: Yellow
        0xFFD7D7D7  // 7: White
    ];

    private static readonly uint[] PaletteBright =
    [
        0xFF000000, // 0: Black
        0xFF0000FF, // 1: Blue
        0xFFFF0000, // 2: Red
        0xFFFF00FF, // 3: Magenta
        0xFF00FF00, // 4: Green
        0xFF00FFFF, // 5: Cyan
        0xFFFFFF00, // 6: Yellow
        0xFFFFFFFF  // 7: White
    ];

    private readonly Ram _ram;

    public ZxSpectrumVideo(Ram ram)
    {
        _ram = ram;
    }

    /// <summary>
    /// Renders the current state of Spectrum VRAM to the sink.
    /// </summary>
    /// <param name="flashInverted">True if flashing characters should be in inverted state.</param>
    public void Render(IVideoSink sink, byte borderColor, bool flashInverted)
    {
        var pixels = new uint[TotalWidth * TotalHeight];
        var ram    = _ram.RawBytes;
        uint borderARGB = PaletteNormal[borderColor & 0x07];

        // 1. Fill the entire 320x240 buffer with the border color
        Array.Fill(pixels, borderARGB);

        // 2. Render the 256x192 active area
        // The Spectrum bitmap is stored in three "thirds" of 64 lines each.
        // Memory Layout: 010 TT SSS RRR CCCCC
        // TT  = Third (0-2)
        // SSS = Scanline within character row (0-7)
        // RRR = Character row within third (0-7)
        // CCCCC = Column (0-31)
        for (int third = 0; third < 3; third++)
        {
            for (int charRow = 0; charRow < 8; charRow++)
            {
                // Optimization: Attributes are constant for all 8 scanlines of a character row.
                // Pre-calculate the paper/ink colors for this row once.
                int attrBase = 0x1800 + ((third << 8) | (charRow << 5));
                
                for (int scanline = 0; scanline < 8; scanline++)
                {
                    int y = (third * 64) + (charRow * 8) + scanline;
                    int bitmapBase = (third << 11) | (scanline << 8) | (charRow << 5);
                    
                    // Offset pixels to center the 256x192 area in 320x240
                    int pixelRowOffset = (y + BorderHeight) * TotalWidth + BorderWidth;

                    for (int col = 0; col < 32; col++)
                    {
                        byte bitmap = ram[bitmapBase + col];
                        byte attr   = ram[attrBase + col];

                        bool bright    = (attr & 0x40) != 0;
                        bool flash     = (attr & 0x80) != 0;
                        int  paperIdx  = (attr >> 3) & 0x07;
                        int  inkIdx    = attr & 0x07;

                        uint[] palette = bright ? PaletteBright : PaletteNormal;
                        uint inkColor  = palette[inkIdx];
                        uint paperColor = palette[paperIdx];

                        if (flash && flashInverted)
                        {
                            (inkColor, paperColor) = (paperColor, inkColor);
                        }

                        // Expand 8 bits to pixels
                        for (int bit = 0; bit < 8; bit++)
                        {
                            bool set = (bitmap & (0x80 >> bit)) != 0;
                            pixels[pixelRowOffset + (col * 8 + bit)] = set ? inkColor : paperColor;
                        }
                    }
                }
            }
        }

        sink.SubmitFrame(pixels, TotalWidth, TotalHeight);
    }
}
