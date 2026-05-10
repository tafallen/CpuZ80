using CpuZ80.Core;
using Machines.Common;

namespace Machines.Zx81;

/// <summary>
/// Handles display file parsing and pixel generation for the Sinclair ZX81.
/// </summary>
public sealed class Zx81Video
{
    private const int   FontOffset = 0x1E00; // ZX81 font is at 0x1E00
    private const uint  Ink        = 0xFF000000u; // black
    private const uint  Paper      = 0xFFFFFFFFu; // white

    private readonly Rom _rom;
    private readonly Ram _ram;

    public Zx81Video(Rom rom, Ram ram)
    {
        _rom = rom;
        _ram = ram;
    }

    /// <summary>
    /// Renders the current display file to the sink as a 256x192 frame.
    /// The ZX81 display file is a series of character codes terminated by HALT (0x76) for each line.
    /// To save RAM, rows can be shorter than 32 characters ("collapsed").
    /// </summary>
    public void Render(IVideoSink sink)
    {
        var pixels = new uint[256 * 192];
        var ram    = _ram.RawBytes;
        var rom    = _rom.RawBytes;

        // D_FILE pointer is at system offset 0x000C (absolute 0x400C).
        ushort dfile = (ushort)(ram[0x000C] | (ram[0x000D] << 8));
        
        // Convert to absolute RAM offset (0x4000-based)
        int pos = dfile - 0x4000; 

        if (pos < 0 || pos >= ram.Length) return;

        pos++; // skip the initial HALT byte

        for (int charRow = 0; charRow < 24; charRow++)
        {
            // Collect up to 32 char codes for this row, stopping at HALT (0x76).
            var rowCodes = new byte[32];
            int colCount = 0;
            while (pos < ram.Length && colCount < 32)
            {
                byte code = ram[pos++];
                if (code == 0x76) goto rowDone; // HALT = end of row
                rowCodes[colCount++] = code;
            }
            // If we hit 32 chars and the next byte isn't HALT, consume until we find it.
            while (pos < ram.Length && ram[pos] != 0x76) pos++;
            if (pos < ram.Length) pos++; // skip the HALT

            rowDone:
            RenderRow(pixels, rowCodes, colCount, charRow, rom);
        }

        sink.SubmitFrame(pixels, 256, 192);
    }

    private void RenderRow(uint[] pixels, byte[] rowCodes, int colCount, int charRow, ReadOnlySpan<byte> rom)
    {
        for (int col = 0; col < 32; col++)
        {
            // Padded columns are white (space character 0x00)
            byte code     = col < colCount ? rowCodes[col] : (byte)0x00;
            int  charBase = code & 0x3F;
            bool inverted = (code & 0x80) != 0;

            int fontOffset = FontOffset + charBase * 8;

            for (int pixRow = 0; pixRow < 8; pixRow++)
            {
                byte fontByte = rom[fontOffset + pixRow];
                int  pixelY   = charRow * 8 + pixRow;

                for (int bit = 0; bit < 8; bit++)
                {
                    bool set    = (fontByte & (0x80 >> bit)) != 0;
                    bool isInk  = set ^ inverted;
                    int  pixelX = col * 8 + bit;
                    pixels[pixelY * 256 + pixelX] = isInk ? Ink : Paper;
                }
            }
        }
    }
}
