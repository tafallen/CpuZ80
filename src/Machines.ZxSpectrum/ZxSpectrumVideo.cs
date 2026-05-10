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
    public void Render(IVideoSink sink, IReadOnlyList<(ulong TState, byte Color)> borderTransitions, byte finalBorderColor, bool flashInverted, ulong frameStartTState)
    {
        var ram = _ram.RawBytes;

        // Border rendering parameters
        const int TStatesPerLine = 224;
        const int TStatesPerPixel = 224 / 320; // 0.7 - wait, horizontal is not a simple div.
        // Spectrum: 224 T-states per line. Border is displayed during certain T-states.
        // For simplicity in a 320x240 view, we assume 1 pixel = 1/320th of a line.

        int transitionIdx = 0;
        byte currentBorder = borderTransitions.Count > 0 ? borderTransitions[0].Color : finalBorderColor;
        
        for (int line = 0; line < TotalHeight; line++)
        {
            int frameLine = line + (312 - TotalHeight) / 2;
            ulong lineStartTState = frameStartTState + (ulong)(frameLine * TStatesPerLine);
            int rowOffset = line * TotalWidth;

            for (int x = 0; x < TotalWidth; x++)
            {
                // Mid-line horizontal border transition sampling
                ulong pixelTState = lineStartTState + (ulong)((x * TStatesPerLine) / TotalWidth);

                while (transitionIdx < borderTransitions.Count && borderTransitions[transitionIdx].TState <= pixelTState)
                {
                    currentBorder = borderTransitions[transitionIdx].Color;
                    transitionIdx++;
                }

                _pixelBuffer[rowOffset + x] = PaletteNormal[currentBorder & 0x07];
            }
        }

        // 2. Overwrite the 256x192 active area
        for (int third = 0; third < 3; third++)
        {
            for (int charRow = 0; charRow < 8; charRow++)
            {
                int attrBase = GetAttributeAddress(third, charRow);
                ReadOnlySpan<byte> rowAttrs = ram.AsSpan(attrBase, 32);
                
                for (int scanline = 0; scanline < 8; scanline++)
                {
                    int y = (third * 64) + (charRow * 8) + scanline;
                    int bitmapBase = GetBitmapAddress(third, charRow, scanline);
                    int pixelRowOffset = (y + BorderHeight) * TotalWidth + BorderWidth;

                    for (int col = 0; col < 32; col++)
                    {
                        byte bitmap = ram[bitmapBase + col];
                        byte attr   = rowAttrs[col];

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBitmapAddress(int third, int charRow, int scanline) => (third << 11) | (scanline << 8) | (charRow << 5);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetAttributeAddress(int third, int charRow) => 0x1800 + (third << 8) | (charRow << 5);
}
