using CpuZ80.Core;
using Machines.Common;
using System.Runtime.CompilerServices;

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
        0xFF000000, // 0: Black (Bright black is still black)
        0xFF0000FF, // 1: Blue
        0xFFFF0000, // 2: Red
        0xFFFF00FF, // 3: Magenta
        0xFF00FF00, // 4: Green
        0xFF00FFFF, // 5: Cyan
        0xFFFFFF00, // 6: Yellow
        0xFFFFFFFF  // 7: White
    ];

    private readonly Ram _ram;
    private readonly uint[] _pixelBuffer = new uint[TotalWidth * TotalHeight];

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
        var ram    = _ram.RawBytes;
        uint borderARGB = PaletteNormal[borderColor & 0x07];

        // 1. Fill the entire 320x240 buffer with the border color
        // This is efficient with the pre-allocated buffer.
        Array.Fill(_pixelBuffer, borderARGB);

        // 2. Render the 256x192 active area
        for (int third = 0; third < 3; third++)
        {
            for (int charRow = 0; charRow < 8; charRow++)
            {
                // Base offset for attributes in this character row
                int attrBase = GetAttributeAddress(third, charRow);
                
                for (int scanline = 0; scanline < 8; scanline++)
                {
                    int y = (third * 64) + (charRow * 8) + scanline;
                    int bitmapBase = GetBitmapAddress(third, charRow, scanline);
                    
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
                            _pixelBuffer[pixelRowOffset + (col * 8 + bit)] = set ? inkColor : paperColor;
                        }
                    }
                }
            }
        }

        sink.SubmitFrame(_pixelBuffer, TotalWidth, TotalHeight);
    }

    /// <summary>
    /// Calculates the bitmap memory offset relative to 0x4000.
    /// Memory Layout: 0 TT SSS RRR CCCCC
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBitmapAddress(int third, int charRow, int scanline)
    {
        return (third << 11) | (scanline << 8) | (charRow << 5);
    }

    /// <summary>
    /// Calculates the attribute memory offset relative to 0x4000.
    /// Memory Layout: 0110 TT RRR CCCCC (Base 0x1800)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetAttributeAddress(int third, int charRow)
    {
        return 0x1800 + (third << 8) | (charRow << 5);
    }
}
