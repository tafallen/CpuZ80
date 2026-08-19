using Xunit;
using CpuZ80.Core;
using Machines.AmstradCpc;
using Machines.Common;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// The video path: pixel decoding for all four modes, and geometry taken from
/// the CRTC rather than assumed.
/// </summary>
/// <remarks>
/// A CPC display byte packs its pixel bits neither adjacently nor in the order
/// intuition suggests: a mode 0 byte is A0 B0 A2 B2 A1 B1 A3 B3 from bit 7 down,
/// where A0 is pixel A's LEAST significant bit. See docs/amstrad-cpc.md §4.
/// </remarks>
public class CpcVideoTests
{
    private sealed class CaptureSink : IVideoSink
    {
        public uint[] Frame = [];
        public int Width, Height;

        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
        {
            Frame = pixels.ToArray();
            Width = width;
            Height = height;
        }
    }

    private static (CpcVideo Video, Mc6845 Crtc, AmstradGateArray Ga, CpcMemory Memory) Build()
    {
        var bus = new AddressDecoder();
        var memory = new CpcMemory(bus, new byte[0x4000], new byte[0x4000]);
        var crtc = new Mc6845();
        var ga = new AmstradGateArray(memory);
        ga.Reset();
        return (new CpcVideo(crtc, ga, memory), crtc, ga, memory);
    }

    private static void SetCrtc(Mc6845 crtc, int register, byte value)
    {
        crtc.Out(0xBC00, (byte)register);   // select
        crtc.Out(0xBD00, value);            // write
    }

    private static void SetInk(AmstradGateArray ga, int pen, byte colour)
    {
        ga.Out(0x7F00, (byte)pen);
        ga.Out(0x7F00, (byte)(0x40 | colour));
    }

    private static void SetMode(AmstradGateArray ga, int mode)
    {
        ga.Out(0x7F00, (byte)(0x80 | mode));
        ga.OnHSync();   // modes latch until the next line
    }

    // ── Mode 3, which had no coverage at all ─────────────────────────────────

    [Theory]
    // Mode 3 keeps mode 0's bits 0 and 1 — byte bits 7 and 3 — and discards the
    // rest. Bit 7 is the pen index's LOW bit.
    [InlineData(0b0000_0000, 0, 0)]
    [InlineData(0b1000_0000, 1, 0)]   // bit 7 -> pixel A bit 0
    [InlineData(0b0000_1000, 2, 0)]   // bit 3 -> pixel A bit 1
    [InlineData(0b1000_1000, 3, 0)]
    [InlineData(0b0100_0000, 0, 1)]   // bit 6 -> pixel B bit 0
    [InlineData(0b0000_0100, 0, 2)]   // bit 2 -> pixel B bit 1
    [InlineData(0b0100_0100, 0, 3)]
    public void Mode3TakesTwoBitsFromModeZerosLayout(byte value, int pixelA, int pixelB)
    {
        int[] pixels = CpcVideo.DecodePixels(value, 3);

        Assert.Equal(2, pixels.Length);
        Assert.Equal(pixelA, pixels[0]);
        Assert.Equal(pixelB, pixels[1]);
    }

    [Fact]
    public void Mode3IgnoresTheBitsModeZeroUsesForItsTopTwo()
    {
        // Bits 5, 4, 1 and 0 carry mode 0's index bits 2 and 3, which mode 3
        // discards. If those leaked in, these two would differ.
        Assert.Equal(CpcVideo.DecodePixels(0b0000_0000, 3), CpcVideo.DecodePixels(0b0011_0011, 3));
    }

    [Fact]
    public void ModeZeroUsesBit7AsTheLeastSignificantBit()
    {
        // The single easiest thing to get backwards. Bit 7 alone is pen 1, not
        // pen 8.
        Assert.Equal(1, CpcVideo.DecodePixels(0b1000_0000, 0)[0]);
        Assert.Equal(8, CpcVideo.DecodePixels(0b0000_0010, 0)[0]);
    }

    // ── Geometry comes from the CRTC ─────────────────────────────────────────

    private static int LitPixels(CaptureSink sink, uint border) =>
        sink.Frame.Count(p => p != border);

    [Fact]
    public void ANarrowerScreenDrawsFewerPixelsAndStaysCentred()
    {
        // Hardcoding the border puts non-standard geometry in the wrong place
        // while still looking plausible.
        var (video, crtc, ga, memory) = Build();
        SetInk(ga, 0, 26);              // a pen that is not the border colour
        SetMode(ga, 1);
        for (int a = 0; a < 0x4000; a++) memory.Banks[3].Write((ushort)a, 0xFF);

        var sink = new CaptureSink();
        uint border = CpcPalette.ToRgba(ga.BorderColour);

        SetCrtc(crtc, 1, 40);
        video.Render(sink);
        int wide = LitPixels(sink, border);
        int wideLeftEdge = FirstLitColumn(sink, border);

        SetCrtc(crtc, 1, 20);
        video.Render(sink);
        int narrow = LitPixels(sink, border);
        int narrowLeftEdge = FirstLitColumn(sink, border);

        Assert.True(narrow < wide, $"20 columns should light fewer pixels than 40, saw {narrow} vs {wide}");
        Assert.True(narrowLeftEdge > wideLeftEdge,
            "a narrower display should start further right, not stay pinned to a fixed border");
    }

    [Fact]
    public void AShorterScreenDrawsFewerRows()
    {
        var (video, crtc, ga, memory) = Build();
        SetInk(ga, 0, 26);
        SetMode(ga, 1);
        for (int a = 0; a < 0x4000; a++) memory.Banks[3].Write((ushort)a, 0xFF);

        var sink = new CaptureSink();
        uint border = CpcPalette.ToRgba(ga.BorderColour);

        SetCrtc(crtc, 6, 25);
        video.Render(sink);
        int tall = LitPixels(sink, border);

        SetCrtc(crtc, 6, 10);
        video.Render(sink);
        int shortScreen = LitPixels(sink, border);

        Assert.True(shortScreen < tall, $"10 rows should light fewer pixels than 25, saw {shortScreen} vs {tall}");
    }

    [Fact]
    public void HardwareScrollingFollowsTheStartAddress()
    {
        // R12/R13 move the display through memory; this is how CPC games scroll
        // without touching a byte of screen data.
        var (video, crtc, ga, memory) = Build();
        SetMode(ga, 1);

        // Pen 0 is left matching the border so the blank screen reads as border
        // and only the test byte stands out. Colouring pen 0 differently makes
        // the whole display "lit" and the measurement meaningless.
        SetInk(ga, 3, 26);

        // One distinctive character's worth of data partway into the screen.
        int address = CpcVideo.DisplayAddress(0x3000 + 5, 0, 0);
        memory.Banks[(address >> 14) & 3].Write((ushort)(address & 0x3FFF), 0xFF);

        var sink = new CaptureSink();
        uint border = CpcPalette.ToRgba(ga.BorderColour);

        SetCrtc(crtc, 12, 0x30);
        SetCrtc(crtc, 13, 0x00);
        video.Render(sink);
        int unscrolled = FirstLitColumn(sink, border);

        SetCrtc(crtc, 13, 0x05);        // scroll five characters
        video.Render(sink);
        int scrolled = FirstLitColumn(sink, border);

        Assert.True(unscrolled >= 0, "the test byte should be visible before scrolling");
        Assert.NotEqual(unscrolled, scrolled);
    }

    [Fact]
    public void OverscanWiderThanTheCanvasIsClippedNotCrashed()
    {
        // Overscan screens legitimately exceed the visible canvas.
        var (video, crtc, ga, memory) = Build();
        SetInk(ga, 0, 26);
        SetMode(ga, 1);
        for (int a = 0; a < 0x4000; a++) memory.Banks[3].Write((ushort)a, 0xFF);

        SetCrtc(crtc, 1, 63);           // far wider than 40 columns
        SetCrtc(crtc, 6, 39);

        var sink = new CaptureSink();
        video.Render(sink);

        Assert.Equal(CpcVideo.Width, sink.Width);
        Assert.Equal(CpcVideo.Height, sink.Height);
    }

    [Fact]
    public void TheBorderColourFillsEverythingTheDisplayDoesNotCover()
    {
        var (video, crtc, ga, _) = Build();

        ga.Out(0x7F00, 0x10);           // select the border
        ga.Out(0x7F00, 0x40 | 6);       // hardware colour 6
        SetCrtc(crtc, 1, 20);

        var sink = new CaptureSink();
        video.Render(sink);

        Assert.Equal(CpcPalette.ToRgba(6), sink.Frame[0]);
    }

    /// <summary>The leftmost column holding anything other than the border, or -1.</summary>
    private static int FirstLitColumn(CaptureSink sink, uint border)
    {
        for (int x = 0; x < sink.Width; x++)
        {
            for (int y = 0; y < sink.Height; y++)
            {
                if (sink.Frame[y * sink.Width + x] != border) return x;
            }
        }
        return -1;
    }
}
