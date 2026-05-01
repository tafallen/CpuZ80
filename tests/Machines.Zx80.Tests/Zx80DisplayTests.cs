using Machines.Common;
using Machines.Zx80;
using Xunit;

namespace Machines.Zx80.Tests;

public class Zx80DisplayTests
{
    private const uint Black = 0xFF000000u;
    private const uint White = 0xFFFFFFFFu;

    // Stub IVideoSink that captures the last submitted frame.
    private sealed class CaptureSink : IVideoSink
    {
        public uint[]? Pixels { get; private set; }
        public int Width  { get; private set; }
        public int Height { get; private set; }

        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
        {
            Pixels = pixels.ToArray();
            Width  = width;
            Height = height;
        }
    }

    // Build a 4K ROM with all NOPs and a specific 8-byte font pattern for character
    // index `charIndex` (0–63) at 0x0E00 + charIndex*8.
    private static byte[] RomWithFont(int charIndex, byte[] fontBytes)
    {
        var rom = new byte[0x1000];
        int offset = 0x0E00 + charIndex * 8;
        fontBytes.CopyTo(rom, offset);
        return rom;
    }

    // Build a minimal valid display file in RAM. Returns a byte[] of size 1K (0x400).
    // D_FILE pointer at 0x000C/0x000D points to 0x4010 (offset 0x10 from RAM base 0x4000)
    // leaving room for system variables. The display file starts at RAM offset 0x10.
    //
    // Structure: HALT, then 24 rows each of [charCodes...] HALT.
    // `rowData` provides the char codes for row 0; all other rows are empty (just HALT).
    private static (byte[] ram, ushort dfileAddr) BuildRam(byte[] row0Chars)
    {
        var ram = new byte[0x400];

        // D_FILE pointer at RAM offset 0x000C = absolute address 0x400C
        const ushort dfileAbsolute = 0x4010;
        ram[0x000C] = (byte)(dfileAbsolute & 0xFF);
        ram[0x000D] = (byte)(dfileAbsolute >> 8);

        // Write display file at RAM offset 0x10
        int pos = 0x10;
        ram[pos++] = 0x76; // initial HALT

        // Row 0: provided chars + HALT
        foreach (byte ch in row0Chars)
            ram[pos++] = ch;
        ram[pos++] = 0x76;

        // Rows 1–23: just HALT each
        for (int row = 1; row < 24; row++)
            ram[pos++] = 0x76;

        return (ram, dfileAbsolute);
    }

    // Load ram bytes into the machine's Ram at base address 0x4000.
    private static void LoadRam(Zx80Machine machine, byte[] ram)
    {
        machine.Ram.Load(0x0000, ram);
    }

    [Fact]
    public void RenderFrame_CorrectDimensions()
    {
        var rom = new byte[0x1000];
        var machine = new Zx80Machine(rom);
        var (ram, _) = BuildRam([]);
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        Assert.Equal(256, sink.Width);
        Assert.Equal(192, sink.Height);
        Assert.Equal(256 * 192, sink.Pixels!.Length);
    }

    [Fact]
    public void RenderFrame_AllSpaces_ProducesWhiteFrame()
    {
        // Space (char 0) has all-zero font bytes → all pixels are paper (white).
        var rom = new byte[0x1000]; // font bytes at 0x0E00 are all 0x00 by default
        var machine = new Zx80Machine(rom);

        // Fill row 0 with 32 spaces (char code 0x00)
        var (ram, _) = BuildRam(Enumerable.Repeat((byte)0x00, 32).ToArray());
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        Assert.All(sink.Pixels!, p => Assert.Equal(White, p));
    }

    [Fact]
    public void RenderFrame_KnownCharacter_CorrectPixelPattern()
    {
        // Use char index 1. Give it a known font: row 0 = 0b10101010 (alternating pixels).
        // Bit 7 = leftmost pixel, so pixels are: ink white white ink white white ink white
        // Wait — ink=black, paper=white. Bit set = ink = black.
        // 0b10101010 → pixels: black white black white black white black white
        const int charIndex = 1;
        var fontRow0 = new byte[] { 0b10101010, 0, 0, 0, 0, 0, 0, 0 };
        var rom = RomWithFont(charIndex, fontRow0);

        var machine = new Zx80Machine(rom);
        var (ram, _) = BuildRam([(byte)charIndex]);
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        // Char 1 is at column 0, row 0. Pixel row 0 of char row 0 starts at pixel index 0.
        uint[] firstRow = sink.Pixels![..8];
        Assert.Equal(Black, firstRow[0]); // bit 7 set
        Assert.Equal(White, firstRow[1]); // bit 6 clear
        Assert.Equal(Black, firstRow[2]); // bit 5 set
        Assert.Equal(White, firstRow[3]); // bit 4 clear
        Assert.Equal(Black, firstRow[4]); // bit 3 set
        Assert.Equal(White, firstRow[5]); // bit 2 clear
        Assert.Equal(Black, firstRow[6]); // bit 1 set
        Assert.Equal(White, firstRow[7]); // bit 0 clear
    }

    [Fact]
    public void RenderFrame_InvertedCharacter_SwapsInkAndPaper()
    {
        // Char code 0x80 = inverted space (base=0, inverted=true).
        // Space font is all zeros → all paper. Inverted → all ink (black).
        var rom = new byte[0x1000]; // font for char 0 stays all zeros
        var machine = new Zx80Machine(rom);
        var (ram, _) = BuildRam([0x80]);
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        // First 8×8 block (char at col 0, row 0) should be all black.
        for (int pixRow = 0; pixRow < 8; pixRow++)
        {
            for (int col = 0; col < 8; col++)
            {
                Assert.Equal(Black, sink.Pixels![pixRow * 256 + col]);
            }
        }
    }
}
