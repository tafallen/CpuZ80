using Xunit;
using CpuZ80.TestSupport;
using Machines.AmstradCpc;
using Machines.Common;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// Booting a real CPC 6128 to its BASIC prompt.
/// </summary>
/// <remarks>
/// The ROM images are gitignored, so these skip when absent — check the test
/// duration, not just that it passed.
/// </remarks>
public class CpcBootTests
{
    internal sealed class CaptureSink : IVideoSink
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

    /// <summary>Loads the 6128 ROM set, or null when the images are not present.</summary>
    internal static CpcMachine? BuildRealMachine()
    {
        string? path = RomLocator.Find("Z80CPC.ROM");
        if (path is null) return null;

        byte[] image = AmsdosHeader.Strip(File.ReadAllBytes(path));

        // The combined image is the OS followed by BASIC.
        byte[] os = image[..0x4000];
        byte[] basic = image[0x4000..0x8000];

        var machine = new CpcMachine(os, basic);
        machine.Reset();
        return machine;
    }

    [Fact]
    public void RealRoms_LoadWithTheirAmsdosHeaderStripped()
    {
        string? path = RomLocator.Find("Z80CPC.ROM");
        if (path is null) return;

        byte[] raw = File.ReadAllBytes(path);
        Assert.Equal(32896, raw.Length);
        Assert.True(AmsdosHeader.HasHeader(raw));

        byte[] stripped = AmsdosHeader.Strip(raw);
        Assert.Equal(32768, stripped.Length);

        // The OS ROM opens LD BC,&7F89 / OUT (C),C — the Gate Array write that
        // sets the initial screen mode.
        Assert.Equal(0x01, stripped[0]);
        Assert.Equal(0x89, stripped[1]);
        Assert.Equal(0x7F, stripped[2]);
        Assert.Equal(0xED, stripped[3]);
        Assert.Equal(0x49, stripped[4]);
    }

    [Fact]
    public void RealRoms_ExecuteWithoutDerailing()
    {
        var machine = BuildRealMachine();
        if (machine is null) return;

        for (int i = 0; i < 100; i++) machine.RunFrame();

        // Still running code, with a sane stack.
        Assert.True(machine.Cpu.TotalCycles > 1_000_000);
        Assert.True(machine.Cpu.SP > 0x1000, $"stack should be sane, was 0x{machine.Cpu.SP:X4}");
    }

    [Fact]
    public void RealRoms_ReachTheBasicPrompt()
    {
        var machine = BuildRealMachine();
        if (machine is null) return;

        for (int i = 0; i < 150; i++) machine.RunFrame();

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        Assert.Equal(CpcVideo.Width, sink.Width);
        Assert.Equal(CpcVideo.Height, sink.Height);

        // Something must actually be on screen.
        var distinct = new HashSet<uint>(sink.Frame);
        Assert.True(distinct.Count >= 2,
            $"the boot screen should have text on a background, saw {distinct.Count} colour(s)");

        // The screen memory must hold non-zero bytes: the banner text.
        int nonZero = CountScreenBytes(machine);
        Assert.True(nonZero > 200,
            $"the BASIC banner should be drawn, but only {nonZero} screen bytes are set");
    }

    [Fact]
    public void RealRoms_SelectTheBootPalette()
    {
        // The 6128 boots to yellow text on a blue background. Pen 0 is hardware
        // colour 4 (navy) and pen 1 is 10 (bright yellow). This is a real check
        // on the palette table as well as on the Gate Array's INKR decode: if
        // either were wrong these two would not land on the documented values.
        var machine = BuildRealMachine();
        if (machine is null) return;

        for (int i = 0; i < 150; i++) machine.RunFrame();

        Assert.Equal(4, machine.GateArray.InkFor(0));
        Assert.Equal(10, machine.GateArray.InkFor(1));
        Assert.Equal(4, machine.GateArray.BorderColour);
        Assert.Equal(1, machine.GateArray.ScreenMode);
    }

    [Fact]
    public void RealRoms_DrawTheBannerAcrossTheFullWidthOfTheScreen()
    {
        // The banner's longest line is the copyright notice, which runs almost
        // the full 40 columns. Sizing the canvas for mode 1's 320 pixels rather
        // than 640 clipped the right-hand half, and a "some pixels are lit"
        // assertion did not notice.
        var machine = BuildRealMachine();
        if (machine is null) return;

        for (int i = 0; i < 150; i++) machine.RunFrame();

        var sink = new CaptureSink();
        machine.RenderFrame(sink);

        uint border = CpcPalette.ToRgba(machine.GateArray.BorderColour);
        int rightmostLit = 0;

        for (int y = 0; y < sink.Height; y++)
        {
            for (int x = 0; x < sink.Width; x++)
            {
                if (sink.Frame[y * sink.Width + x] != border) rightmostLit = Math.Max(rightmostLit, x);
            }
        }

        Assert.True(rightmostLit > sink.Width / 2,
            $"the copyright line should reach past halfway, but nothing is lit beyond x={rightmostLit}");
    }

    [Fact]
    public void AcceptingAnInterrupt_ClearsTheInterruptLine()
    {
        // INT is level-triggered and the Gate Array holds it until acknowledged.
        // Leaving it asserted re-enters the handler the instant it returns, so
        // the machine spins in the ISR forever — with a healthy stack and a
        // plausible PC, which is exactly why it did not look like a crash.
        //
        // Asserted on the CPU's acknowledgement rather than on a frame loop, so
        // a bare Step() loop (what a debugger does) is covered too.
        var machine = new CpcMachine(TestRom(), TestRom());
        machine.Reset();
        machine.Cpu.IFF1 = true;
        machine.Cpu.IM = 1;

        machine.Cpu.IntPin = true;
        machine.Step();

        Assert.False(machine.Cpu.IntPin, "the Gate Array should drop INT once the CPU acknowledges");
        Assert.Equal(0x0038, machine.Cpu.PC);
    }

    /// <summary>A 16K ROM of NOPs, for tests that do not need real firmware.</summary>
    internal static byte[] TestRom() => new byte[0x4000];

    internal static int CountScreenBytes(CpcMachine machine)
    {
        // The default screen sits in the top 16K of the base 64K, which is
        // bank 3 in the default configuration.
        var bank = machine.Memory.Banks[3];
        int nonZero = 0;
        for (int a = 0; a < 0x4000; a++) if (bank.Read((ushort)a) != 0) nonZero++;
        return nonZero;
    }
}
