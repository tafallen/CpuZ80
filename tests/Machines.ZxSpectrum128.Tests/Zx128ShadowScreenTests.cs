using Xunit;
using Machines.Common;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// Bit 3 of port 0x7FFD selects which bank the ULA displays: 5 (normal) or 7
/// (shadow). This is independent of what the CPU has paged at 0xC000, so a
/// program can draw into one bank while the other is on screen.
/// </summary>
public class Zx128ShadowScreenTests
{
    private const int TotalWidth = 320;
    private const int BorderWidth = 32;
    private const int BorderHeight = 24;

    private sealed class CaptureSink : IVideoSink
    {
        public uint[] Frame = [];
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => Frame = pixels.ToArray();
    }

    private static int ActiveAreaIndex(int x, int y) => ((y + BorderHeight) * TotalWidth) + BorderWidth + x;

    /// <summary>Fills a bank's screen with solid ink of <paramref name="colour"/>.</summary>
    private static void PaintBank(Zx128Machine machine, int bank, byte colour)
    {
        var ram = machine.Banks[bank];
        for (ushort a = 0x0000; a < 0x1800; a++) ram.Write(a, 0xFF);          // all pixels set
        for (ushort a = 0x1800; a < 0x1B00; a++) ram.Write(a, colour);        // attributes
    }

    private static uint[] Render(Zx128Machine machine)
    {
        var sink = new CaptureSink();
        machine.RenderFrame(sink);
        return sink.Frame;
    }

    private static Zx128Machine Machine()
    {
        var m = new Zx128Machine(new byte[0x8000]);
        m.Reset();
        return m;
    }

    // Bright ink colours, RGBA (0xAABBGGRR): red = 0xFF0000FF, blue = 0xFFFF0000.
    private const byte BrightRedInk = 0x42;
    private const byte BrightBlueInk = 0x41;
    private const uint BrightRed = 0xFF0000FFu;
    private const uint BrightBlue = 0xFFFF0000u;

    [Fact]
    public void ByDefault_Bank5IsDisplayed()
    {
        var machine = Machine();
        PaintBank(machine, 5, BrightRedInk);
        PaintBank(machine, 7, BrightBlueInk);

        Assert.Equal(BrightRed, Render(machine)[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void Bit3_SwitchesTheDisplayToBank7()
    {
        var machine = Machine();
        PaintBank(machine, 5, BrightRedInk);
        PaintBank(machine, 7, BrightBlueInk);

        machine.WritePort(0x7FFD, 0x08);

        Assert.Equal(7, machine.Pager.ScreenBank);
        Assert.Equal(BrightBlue, Render(machine)[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void ClearingBit3_SwitchesBack()
    {
        var machine = Machine();
        PaintBank(machine, 5, BrightRedInk);
        PaintBank(machine, 7, BrightBlueInk);

        machine.WritePort(0x7FFD, 0x08);
        Assert.Equal(BrightBlue, Render(machine)[ActiveAreaIndex(0, 0)]);

        machine.WritePort(0x7FFD, 0x00);
        Assert.Equal(BrightRed, Render(machine)[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void DisplayedBank_IsIndependentOfThePagedBank()
    {
        // Show bank 7 while bank 0 is paged at 0xC000 — the classic
        // double-buffering arrangement.
        var machine = Machine();
        PaintBank(machine, 5, BrightRedInk);
        PaintBank(machine, 7, BrightBlueInk);

        machine.WritePort(0x7FFD, 0x08); // screen 7, paged bank 0

        Assert.Equal(7, machine.Pager.ScreenBank);
        Assert.Equal(0, machine.Pager.PagedBank);
        Assert.Equal(BrightBlue, Render(machine)[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void WritingThroughThePagedWindow_UpdatesTheDisplayedShadowScreen()
    {
        // Page bank 7 in at 0xC000 and draw into it while it is displayed.
        var machine = Machine();
        PaintBank(machine, 5, BrightRedInk);
        PaintBank(machine, 7, BrightRedInk);

        machine.WritePort(0x7FFD, 0x08 | 0x07); // screen 7 and bank 7 at 0xC000

        // Repaint attributes through the CPU's view of the bank.
        for (ushort a = 0x1800; a < 0x1B00; a++)
        {
            machine.WriteMemory((ushort)(0xC000 + a), BrightBlueInk);
        }

        Assert.Equal(BrightBlue, Render(machine)[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void ScreenBankSurvivesAResetToBank5()
    {
        var machine = Machine();
        PaintBank(machine, 5, BrightRedInk);
        PaintBank(machine, 7, BrightBlueInk);

        machine.WritePort(0x7FFD, 0x08);
        Assert.Equal(BrightBlue, Render(machine)[ActiveAreaIndex(0, 0)]);

        machine.Reset();
        PaintBank(machine, 5, BrightRedInk);

        Assert.Equal(5, machine.Pager.ScreenBank);
        Assert.Equal(BrightRed, Render(machine)[ActiveAreaIndex(0, 0)]);
    }

    [Fact]
    public void FloatingBus_FollowsTheDisplayedBank()
    {
        // The ULA reads attributes from whichever bank it is displaying, so an IN
        // from an unattached port must see the shadow screen's attributes too.
        var machine = Machine();
        for (ushort a = 0x1800; a < 0x1B00; a++)
        {
            machine.Banks[5].Write(a, 0x11);
            machine.Banks[7].Write(a, 0x22);
        }

        machine.Cpu.TotalCycles = (ulong)(Machines.ZxSpectrum.UlaTiming.Spectrum128.ContentionStart + 2);
        Assert.Equal(0x11, machine.ReadPort(0x00FF));

        machine.WritePort(0x7FFD, 0x08);
        machine.Cpu.TotalCycles = (ulong)(Machines.ZxSpectrum.UlaTiming.Spectrum128.ContentionStart + 2);
        Assert.Equal(0x22, machine.ReadPort(0x00FF));
    }
}
