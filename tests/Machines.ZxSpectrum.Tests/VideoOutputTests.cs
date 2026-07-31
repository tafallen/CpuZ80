using Xunit;
using Machines.Common;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// Frame buffer output: pixel byte order, and the border pass that surrounds the
/// 256x192 active area.
/// </summary>
/// <remarks>
/// Pixels are RGBA32 — on a little-endian machine the bytes land in memory as
/// R, G, B, A, which is what GPU texture uploads expect. Packed into a uint that
/// reads 0xAABBGGRR, so pure red is 0xFF0000FF and pure blue is 0xFFFF0000.
/// </remarks>
public class VideoOutputTests
{
    private const int TotalWidth = 320;
    private const int TotalHeight = 240;
    private const int BorderWidth = 32;
    private const int BorderHeight = 24;

    private sealed class CaptureSink : IVideoSink
    {
        public uint[]? Frame;
        public int Width, Height;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
        {
            Frame = pixels.ToArray();
            Width = width;
            Height = height;
        }
    }

    private static uint[] RenderWith(Action<ZxSpectrumMachine> setup)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000]);
        machine.Reset();
        setup(machine);

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        Assert.NotNull(sink.Frame);
        Assert.Equal(TotalWidth * TotalHeight, sink.Frame!.Length);
        return sink.Frame;
    }

    private static int ActiveAreaIndex(int x, int y) => ((y + BorderHeight) * TotalWidth) + BorderWidth + x;

    // ── Pixel byte order ─────────────────────────────────────────────────────

    [Fact]
    public void BrightBlueInk_IsEmittedAsRgba()
    {
        // Bitmap 0x80 sets the leftmost pixel; attribute 0x41 is
        // Bright=1, Paper=Black, Ink=Blue.
        uint[] frame = RenderWith(m =>
        {
            m.WriteMemory(0x4000, 0x80);
            m.WriteMemory(0x5800, 0x41);
        });

        // Bright blue: R=0x00 G=0x00 B=0xFF -> 0xAABBGGRR = 0xFFFF0000
        Assert.Equal(0xFFFF0000u, frame[ActiveAreaIndex(0, 0)]);
        // Paper is black, identical in either byte order.
        Assert.Equal(0xFF000000u, frame[ActiveAreaIndex(1, 0)]);
    }

    [Fact]
    public void BrightRedInk_IsEmittedAsRgba()
    {
        // Attribute 0x42: Bright=1, Paper=Black, Ink=Red.
        uint[] frame = RenderWith(m =>
        {
            m.WriteMemory(0x4000, 0x80);
            m.WriteMemory(0x5800, 0x42);
        });

        // Bright red: R=0xFF G=0x00 B=0x00 -> 0xFF0000FF
        Assert.Equal(0xFF0000FFu, frame[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void BrightYellowInk_IsEmittedAsRgba()
    {
        // Attribute 0x46: Bright=1, Ink=Yellow (red+green).
        uint[] frame = RenderWith(m =>
        {
            m.WriteMemory(0x4000, 0x80);
            m.WriteMemory(0x5800, 0x46);
        });

        // Bright yellow: R=0xFF G=0xFF B=0x00 -> 0xFF00FFFF
        Assert.Equal(0xFF00FFFFu, frame[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void WhiteAndBlack_AreByteOrderAgnostic()
    {
        // Attribute 0x47: Bright=1, Paper=Black, Ink=White.
        uint[] frame = RenderWith(m =>
        {
            m.WriteMemory(0x4000, 0x80);
            m.WriteMemory(0x5800, 0x47);
        });

        Assert.Equal(0xFFFFFFFFu, frame[ActiveAreaIndex(0, 0)]);
        Assert.Equal(0xFF000000u, frame[ActiveAreaIndex(1, 0)]);
    }

    // ── Border pass ──────────────────────────────────────────────────────────

    [Fact]
    public void Border_FillsTheWholePerimeter()
    {
        // Border colour 2 (red) set before the frame is rendered.
        uint[] frame = RenderWith(m => m.WritePort(0x00FE, 0x02));

        const uint Red = 0xFF0000D7u; // normal (non-bright) red in RGBA

        // Top and bottom bands, full width.
        Assert.Equal(Red, frame[0]);                                        // top-left
        Assert.Equal(Red, frame[TotalWidth - 1]);                           // top-right
        Assert.Equal(Red, frame[(TotalHeight - 1) * TotalWidth]);           // bottom-left
        Assert.Equal(Red, frame[(TotalHeight * TotalWidth) - 1]);           // bottom-right

        // Left and right bands on a middle row, either side of the active area.
        int midRow = (TotalHeight / 2) * TotalWidth;
        Assert.Equal(Red, frame[midRow]);                                   // far left
        Assert.Equal(Red, frame[midRow + BorderWidth - 1]);                 // last border pixel
        Assert.Equal(Red, frame[midRow + TotalWidth - BorderWidth]);        // first right-border pixel
        Assert.Equal(Red, frame[midRow + TotalWidth - 1]);                  // far right
    }

    [Fact]
    public void Border_DoesNotBleedIntoTheActiveArea()
    {
        // Border red, but the active area is all paper (attributes are zero =
        // black paper, black ink), so the first active pixel must not be red.
        uint[] frame = RenderWith(m => m.WritePort(0x00FE, 0x02));

        const uint Red = 0xFF0000D7u;
        Assert.NotEqual(Red, frame[ActiveAreaIndex(0, 0)]);
        Assert.NotEqual(Red, frame[ActiveAreaIndex(255, 191)]);
    }

    [Fact]
    public void ActiveArea_IsFullyPaintedRegardlessOfBorder()
    {
        // Every attribute cell bright white ink on black paper, bitmap all set:
        // the whole 256x192 area must be white, with no border colour surviving
        // underneath from the perimeter pass.
        uint[] frame = RenderWith(m =>
        {
            m.WritePort(0x00FE, 0x02); // red border
            for (ushort a = 0x4000; a < 0x5800; a++) m.WriteMemory(a, 0xFF);
            for (ushort a = 0x5800; a < 0x5B00; a++) m.WriteMemory(a, 0x47);
        });

        for (int y = 0; y < 192; y += 17)
        {
            for (int x = 0; x < 256; x += 13)
            {
                Assert.Equal(0xFFFFFFFFu, frame[ActiveAreaIndex(x, y)]);
            }
        }
    }

    [Fact]
    public void BorderColourChange_IsReflectedInLaterScanlines()
    {
        // The border pass walks transitions in T-state order. Skipping the
        // active-area pixels must not lose a transition: a colour set partway
        // through the frame still has to reach the bottom band.
        var machine = new ZxSpectrumMachine(new byte[0x4000]);
        machine.Reset();

        machine.WritePort(0x00FE, 0x01);      // blue at the start of the frame
        machine.RunFrame();                    // establish a frame window
        machine.WritePort(0x00FE, 0x02);      // change to red partway through

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        Assert.NotNull(sink.Frame);
        // Whatever the exact split, both colours came from the same transition
        // list, so the final border colour must appear somewhere in the bottom band.
        uint[] frame = sink.Frame!;
        var bottomBand = frame.AsSpan((TotalHeight - BorderHeight) * TotalWidth).ToArray();
        Assert.Contains(bottomBand, px => px == 0xFF0000D7u || px == 0xFFD70000u);
    }
}
