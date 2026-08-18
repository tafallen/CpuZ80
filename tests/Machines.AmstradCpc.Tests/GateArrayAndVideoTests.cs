using Xunit;
using CpuZ80.Core;
using Machines.AmstradCpc;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// Gate Array register decoding, and the Gate Array's pixel packing.
/// </summary>
/// <remarks>
/// Two published references disagree about which data bits select which
/// register; see docs/amstrad-cpc.md §2.1. These tests pin the resolved
/// answer down so it cannot drift back.
/// </remarks>
public class GateArrayAndVideoTests
{
    private const ushort Port = 0x7F00;

    private static (AmstradGateArray Ga, CpcMemory Memory) Build()
    {
        var bus = new AddressDecoder();
        var memory = new CpcMemory(bus, new byte[0x4000], new byte[0x4000]);
        var ga = new AmstradGateArray(memory);
        ga.Reset();
        return (ga, memory);
    }

    // ── Register selection ───────────────────────────────────────────────────

    [Fact]
    public void PenAndInk_AssignAColourToAPen()
    {
        var (ga, _) = Build();

        ga.Out(Port, 0x02);          // 00: select pen 2
        ga.Out(Port, 0x40 | 0x0A);   // 01: assign hardware colour 10

        Assert.Equal(10, ga.InkFor(2));
    }

    [Fact]
    public void PenBit4SelectsTheBorder()
    {
        var (ga, _) = Build();

        ga.Out(Port, 0x10);          // select the border
        ga.Out(Port, 0x40 | 0x04);

        Assert.Equal(4, ga.BorderColour);
        Assert.NotEqual(4, ga.InkFor(0));
    }

    [Theory]
    [InlineData(0x80, 0)]
    [InlineData(0x81, 1)]
    [InlineData(0x82, 2)]
    [InlineData(0x83, 3)]
    public void Rmr_SetsTheScreenMode(byte value, int mode)
    {
        // 10, not 11. One published reference has these swapped, and choosing
        // wrongly puts the machine in the wrong mode on the OS ROM's first
        // instruction — which presents as a video bug, not a decoding one.
        var (ga, _) = Build();

        ga.Out(Port, value);
        ga.OnHSync();          // the mode is latched until the next HSync

        Assert.Equal(mode, ga.ScreenMode);
    }

    [Fact]
    public void AModeChangeIsLatchedUntilTheNextHSync()
    {
        // Software changes mode mid-frame for split screens and relies on the
        // change landing on a line boundary. Applying it the instant the port
        // is written tears the line the write happens on.
        var (ga, _) = Build();
        ga.Out(Port, 0x81);
        ga.OnHSync();
        Assert.Equal(1, ga.ScreenMode);

        ga.Out(Port, 0x80);                    // ask for mode 0 mid-line

        Assert.Equal(1, ga.ScreenMode);        // not yet
        Assert.Equal(0, ga.PendingScreenMode);

        ga.OnHSync();

        Assert.Equal(0, ga.ScreenMode);        // now
    }

    [Fact]
    public void OnlyTheLastModeWrittenBeforeAnHSyncTakesEffect()
    {
        var (ga, _) = Build();

        ga.Out(Port, 0x80);
        ga.Out(Port, 0x82);
        ga.OnHSync();

        Assert.Equal(2, ga.ScreenMode);
    }

    [Fact]
    public void Rmr_RomEnablesAreActiveLow()
    {
        // A SET bit disables. Inverting this maps RAM where the OS expects ROM
        // and the machine dies before it draws anything.
        var (ga, memory) = Build();

        ga.Out(Port, 0x80);                  // both bits clear
        Assert.True(memory.LowerRomEnabled);
        Assert.True(memory.UpperRomEnabled);

        ga.Out(Port, 0x80 | 0x04);           // bit 2 set: lower ROM off
        Assert.False(memory.LowerRomEnabled);
        Assert.True(memory.UpperRomEnabled);

        ga.Out(Port, 0x80 | 0x08);           // bit 3 set: upper ROM off
        Assert.True(memory.LowerRomEnabled);
        Assert.False(memory.UpperRomEnabled);
    }

    [Fact]
    public void Rmr_Bit4ResetsTheInterruptCounter()
    {
        var (ga, _) = Build();
        for (int i = 0; i < 10; i++) ga.OnHSync();
        Assert.Equal(10, ga.RasterCounter);

        ga.Out(Port, 0x80 | 0x10);

        Assert.Equal(0, ga.RasterCounter);
    }

    [Fact]
    public void Mmr_SetsTheRamConfiguration()
    {
        var (ga, memory) = Build();

        ga.Out(Port, 0xC0 | 0x03);

        Assert.Equal(3, memory.RamConfig);
    }

    [Fact]
    public void OnlyAnswersWhenA15IsClearAndA14Set()
    {
        var (ga, _) = Build();

        ga.Out(0xFF00, 0x83);   // A15 set: not ours
        ga.OnHSync();
        Assert.Equal(1, ga.ScreenMode);

        ga.Out(0x3F00, 0x83);   // A14 clear: not ours
        ga.OnHSync();
        Assert.Equal(1, ga.ScreenMode);

        ga.Out(0x7F00, 0x83);
        ga.OnHSync();
        Assert.Equal(3, ga.ScreenMode);
    }

    // ── Interrupts ───────────────────────────────────────────────────────────

    [Fact]
    public void InterruptFiresEvery52HSyncs()
    {
        var (ga, _) = Build();
        int fired = 0;
        ga.InterruptRequested += () => fired++;

        for (int i = 0; i < 52 * 6; i++) ga.OnHSync();

        // 300 Hz on a PAL machine: six per 50 Hz frame, not one.
        Assert.Equal(6, fired);
    }

    [Fact]
    public void VSyncResynchronisesTheCounterTwoHSyncsLater()
    {
        // The Gate Array keeps interrupts in step with the frame by resetting
        // the counter two HSyncs after VSync begins, rather than letting it
        // free-run and drift against the display.
        var (ga, _) = Build();
        for (int i = 0; i < 10; i++) ga.OnHSync();
        Assert.Equal(10, ga.RasterCounter);

        ga.OnVSync();
        ga.OnHSync();
        Assert.Equal(11, ga.RasterCounter);   // still counting

        ga.OnHSync();
        Assert.Equal(0, ga.RasterCounter);    // resynchronised
    }

    [Fact]
    public void TheVSyncResyncIssuesAnInterruptWhenTheCounterIsBelow32()
    {
        var (ga, _) = Build();
        int fired = 0;
        ga.InterruptRequested += () => fired++;

        for (int i = 0; i < 10; i++) ga.OnHSync();   // counter well under 32
        ga.OnVSync();
        ga.OnHSync();
        ga.OnHSync();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void TheVSyncResyncSuppressesTheInterruptWhenBit5IsSet()
    {
        // A counter at 32 or above is already close to its own interrupt;
        // firing here as well would double up.
        var (ga, _) = Build();
        for (int i = 0; i < 40; i++) ga.OnHSync();
        Assert.True((ga.RasterCounter & 0x20) != 0);

        int fired = 0;
        ga.InterruptRequested += () => fired++;

        ga.OnVSync();
        ga.OnHSync();
        ga.OnHSync();

        Assert.Equal(0, fired);
        Assert.Equal(0, ga.RasterCounter);
    }

    // ── Pixel packing ────────────────────────────────────────────────────────

    [Fact]
    public void Mode2IsLinear()
    {
        Assert.Equal(new[] { 1, 0, 1, 0, 0, 1, 0, 1 }, CpcVideo.DecodePixels(0b1010_0101, 2));
    }

    [Fact]
    public void Mode1PixelBitsAreInterleavedNotAdjacent()
    {
        // Byte 0b1000_0001: the high bit is pixel 0's high bit and bit 3 is
        // pixel 0's low bit. Taking contiguous pairs would give 2,0,0,1 and
        // produce a display that looks almost right.
        int[] pixels = CpcVideo.DecodePixels(0b1000_1000, 1);

        Assert.Equal(new[] { 3, 0, 0, 0 }, pixels);
    }

    [Fact]
    public void Mode0PixelBitsAreSpreadAcrossTheByte()
    {
        // All four bits of pixel 0 are at positions 7, 3, 5 and 1.
        int[] pixels = CpcVideo.DecodePixels(0b1010_1010, 0);

        Assert.Equal(2, pixels.Length);
        Assert.Equal(15, pixels[0]);
        Assert.Equal(0, pixels[1]);
    }

    [Fact]
    public void EachModeCoversTheSameNumberOfBytesPerLine()
    {
        // Whatever the mode, one byte is one byte; the pixel count differs.
        Assert.Equal(2, CpcVideo.DecodePixels(0xFF, 0).Length);
        Assert.Equal(4, CpcVideo.DecodePixels(0xFF, 1).Length);
        Assert.Equal(8, CpcVideo.DecodePixels(0xFF, 2).Length);
    }

    // ── Display addressing ───────────────────────────────────────────────────

    [Fact]
    public void ConsecutiveScanlinesAre0x800Apart()
    {
        // The screen is not a linear frame buffer: the scanline within a
        // character row is added at bit 11.
        int line0 = CpcVideo.DisplayAddress(0x3000, 0, 0);
        int line1 = CpcVideo.DisplayAddress(0x3000, 1, 0);

        Assert.Equal(0x800, line1 - line0);
    }

    [Fact]
    public void TheStartAddressTopBitsSelectA16KPage()
    {
        // R12 bits 5-4 become A15-14, which is what puts the default screen at
        // 0xC000.
        Assert.Equal(0x0000, CpcVideo.DisplayAddress(0x0000, 0, 0) & 0xC000);
        Assert.Equal(0xC000, CpcVideo.DisplayAddress(0x3000, 0, 0) & 0xC000);
    }

    [Fact]
    public void EachCrtcCharacterIsTwoBytes()
    {
        int first = CpcVideo.DisplayAddress(0x3000, 0, 0);
        int second = CpcVideo.DisplayAddress(0x3000, 0, 1);
        int nextChar = CpcVideo.DisplayAddress(0x3001, 0, 0);

        Assert.Equal(first + 1, second);
        Assert.Equal(first + 2, nextChar);
    }
}
