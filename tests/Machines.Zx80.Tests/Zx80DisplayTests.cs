using Machines.Common;
using Machines.Zx80;
using Xunit;

namespace Machines.Zx80.Tests;

public class Zx80DisplayTests
{
    private const uint Black = 0xFF000000u;
    private const uint White = 0xFFFFFFFFu;

    private const int TotalWidth = 320;
    private const int TotalHeight = 240;
    private const int BorderWidth = 32;
    private const int BorderHeight = 24;

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

    private static byte[] RomWithFont(int charIndex, byte[] fontBytes)
    {
        var rom = new byte[0x1000];
        int offset = 0x0E00 + charIndex * 8;
        fontBytes.CopyTo(rom, offset);
        return rom;
    }

    private static (byte[] ram, ushort dfileAddr) BuildRam(byte[] row0Chars)
    {
        var ram = new byte[0x400];
        const ushort dfileAbsolute = 0x4010;
        ram[0x000C] = (byte)(dfileAbsolute & 0xFF);
        ram[0x000D] = (byte)(dfileAbsolute >> 8);
        int pos = 0x10;
        ram[pos++] = 0x76;
        foreach (byte ch in row0Chars)
            ram[pos++] = ch;
        ram[pos++] = 0x76;
        for (int row = 1; row < 24; row++)
            ram[pos++] = 0x76;
        return (ram, dfileAbsolute);
    }

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

        Assert.Equal(TotalWidth, sink.Width);
        Assert.Equal(TotalHeight, sink.Height);
        Assert.Equal(TotalWidth * TotalHeight, sink.Pixels!.Length);
    }

    [Fact]
    public void RenderFrame_AllSpaces_ProducesWhiteFrame()
    {
        var rom = new byte[0x1000];
        var machine = new Zx80Machine(rom);
        var (ram, _) = BuildRam(Enumerable.Repeat((byte)0x00, 32).ToArray());
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        Assert.All(sink.Pixels!, p => Assert.Equal(White, p));
    }

    [Fact]
    public void RenderFrame_KnownCharacter_CorrectPixelPattern()
    {
        const int charIndex = 1;
        var fontRow0 = new byte[] { 0b10101010, 0, 0, 0, 0, 0, 0, 0 };
        var rom = RomWithFont(charIndex, fontRow0);

        var machine = new Zx80Machine(rom);
        var (ram, _) = BuildRam([(byte)charIndex]);
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        // Char 1 is at col 0, row 0 of active area.
        // Active area starts at (32, 24) in 320x240 buffer.
        int activeAreaStart = (BorderHeight * TotalWidth) + BorderWidth;
        
        uint[] firstRow = sink.Pixels!.AsSpan(activeAreaStart, 8).ToArray();
        Assert.Equal(Black, firstRow[0]);
        Assert.Equal(White, firstRow[1]);
        Assert.Equal(Black, firstRow[2]);
        Assert.Equal(White, firstRow[3]);
        Assert.Equal(Black, firstRow[4]);
        Assert.Equal(White, firstRow[5]);
        Assert.Equal(Black, firstRow[6]);
        Assert.Equal(White, firstRow[7]);
    }

    [Fact]
    public void RenderFrame_InvertedCharacter_SwapsInkAndPaper()
    {
        var rom = new byte[0x1000];
        var machine = new Zx80Machine(rom);
        var (ram, _) = BuildRam([0x80]);
        LoadRam(machine, ram);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        int activeAreaStart = (BorderHeight * TotalWidth) + BorderWidth;

        for (int pixRow = 0; pixRow < 8; pixRow++)
        {
            for (int col = 0; col < 8; col++)
            {
                Assert.Equal(Black, sink.Pixels![activeAreaStart + (pixRow * TotalWidth) + col]);
            }
        }
    }
}
