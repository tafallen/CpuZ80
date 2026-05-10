using CpuZ80.Core;
using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// Shared logic for Sinclair-style character rendering (ZX80, ZX81).
/// </summary>
public sealed class SinclairVideo
{
    private const uint Ink   = 0xFF000000u; // black
    private const uint Paper = 0xFFFFFFFFu; // white

    private readonly Rom _rom;
    private readonly Ram _ram;
    private readonly int _fontOffset;

    public SinclairVideo(Rom rom, Ram ram, int fontOffset)
    {
        _rom        = rom;
        _ram        = ram;
        _fontOffset = fontOffset;
    }

    /// <summary>
    /// Renders a Sinclair display file to the sink.
    /// Supports collapsed rows (shorter than 32 characters) terminated by HALT (0x76).
    /// </summary>
    public void Render(IVideoSink sink)
    {
        var pixels = new uint[256 * 192];
        var ram    = _ram.RawBytes;
        var rom    = _rom.RawBytes;

        // D_FILE is a 16-bit little-endian pointer at system offset 0x000C (absolute 0x400C).
        ushort dfile = (ushort)(ram[0x000C] | (ram[0x000D] << 8));
        
        // Convert to absolute RAM offset (0x4000-based)
        int pos = dfile - 0x4000; 

        if (pos < 0 || pos >= ram.Length) return;

        pos++; // skip the initial HALT byte

        for (int charRow = 0; charRow < 24; charRow++)
        {
            var rowCodes = new byte[32];
            int colCount = 0;
            while (pos < ram.Length && colCount < 32)
            {
                byte code = ram[pos++];
                if (code == 0x76) goto rowDone; // HALT = end of row
                rowCodes[colCount++] = code;
            }
            // Consume HALT if we hit the column limit without seeing one.
            while (pos < ram.Length && ram[pos] != 0x76) pos++;
            if (pos < ram.Length) pos++; 

            rowDone:
            RenderRow(pixels, rowCodes, colCount, charRow, rom);
        }

        sink.SubmitFrame(pixels, 256, 192);
    }

    private void RenderRow(uint[] pixels, byte[] rowCodes, int colCount, int charRow, ReadOnlySpan<byte> rom)
    {
        for (int col = 0; col < 32; col++)
        {
            byte code     = col < colCount ? rowCodes[col] : (byte)0x00;
            int  charBase = code & 0x3F;
            bool inverted = (code & 0x80) != 0;

            int glyphOffset = _fontOffset + charBase * 8;

            for (int pixRow = 0; pixRow < 8; pixRow++)
            {
                byte fontByte = rom[glyphOffset + pixRow];
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
