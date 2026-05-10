using CpuZ80.Core;
using Machines.Common;
using System.Runtime.CompilerServices;

namespace Machines.Sinclair.Common;

/// <summary>
/// Handles Sinclair-style character rendering (ZX80, ZX81) with border support.
/// Renders to a 320x240 buffer to match the Spectrum high-fidelity standard.
/// </summary>
public sealed class SinclairVideo
{
    public const int TotalWidth = 320;
    public const int TotalHeight = 240;
    public const int ActiveWidth = 256;
    public const int ActiveHeight = 192;
    public const int BorderWidth = (TotalWidth - ActiveWidth) / 2;
    public const int BorderHeight = (TotalHeight - ActiveHeight) / 2;

    private const uint Ink   = 0xFF000000u; // black
    private const uint Paper = 0xFFFFFFFFu; // white

    private readonly Rom _rom;
    private readonly Ram _ram;
    private readonly int _fontOffset;
    private readonly uint[] _pixelBuffer = new uint[TotalWidth * TotalHeight];
    private readonly byte[] _rowBuffer   = new byte[32];

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
        var ram    = _ram.RawBytes;
        var rom    = _rom.RawBytes;

        // Sinclair (ZX80/81) uses a static white border. 
        // We fill the entire buffer with Paper (White) first.
        Array.Fill(_pixelBuffer, Paper);

        // D_FILE is a 16-bit little-endian pointer at system offset 0x000C (absolute 0x400C).
        ushort dfile = (ushort)(ram[0x000C] | (ram[0x000D] << 8));
        int pos = dfile - 0x4000; 

        if (pos < 0 || pos >= ram.Length)
        {
            sink.SubmitFrame(_pixelBuffer, TotalWidth, TotalHeight);
            return;
        }

        pos++; // skip initial HALT

        for (int charRow = 0; charRow < 24; charRow++)
        {
            Array.Clear(_rowBuffer);
            int colCount = 0;
            while (pos < ram.Length && colCount < 32)
            {
                byte code = ram[pos++];
                if (code == 0x76) goto rowDone;
                _rowBuffer[colCount++] = code;
            }
            while (pos < ram.Length && ram[pos] != 0x76) pos++;
            if (pos < ram.Length) pos++; 

            rowDone:
            RenderRow(_rowBuffer, colCount, charRow, rom);
        }

        sink.SubmitFrame(_pixelBuffer, TotalWidth, TotalHeight);
    }

    private void RenderRow(byte[] rowCodes, int colCount, int charRow, ReadOnlySpan<byte> rom)
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
                int  pixelRowOffset = (pixelY + BorderHeight) * TotalWidth + BorderWidth;

                for (int bit = 0; bit < 8; bit++)
                {
                    bool set    = (fontByte & (0x80 >> bit)) != 0;
                    bool isInk  = set ^ inverted;
                    _pixelBuffer[pixelRowOffset + (col * 8 + bit)] = isInk ? Ink : Paper;
                }
            }
        }
    }
}
