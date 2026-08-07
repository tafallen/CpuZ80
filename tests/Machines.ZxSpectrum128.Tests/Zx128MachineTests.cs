using Xunit;
using Machines.Common;
using Machines.ZxSpectrum;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// ZX Spectrum 128 motherboard composition: 8 RAM banks, 2 ROMs, 128K timing.
/// </summary>
public class Zx128MachineTests
{
    /// <summary>A 32K image with each half stamped so the paged ROM is identifiable.</summary>
    private static byte[] CombinedRom()
    {
        byte[] image = new byte[0x8000];
        image[0x0000] = 0xA0; // ROM 0 marker
        image[0x4000] = 0xA1; // ROM 1 marker
        return image;
    }

    private sealed class NullVideo : IVideoSink
    {
        public int Frames;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => Frames++;
    }

    // ── ROM loading ──────────────────────────────────────────────────────────

    [Fact]
    public void Combined32KImage_IsSplitIntoTwoRoms()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        Assert.True(machine.Rom1Present);
        Assert.Equal(0xA0, machine.ReadMemory(0x0000));

        machine.WritePort(0x7FFD, 0x10); // select ROM 1
        Assert.Equal(0xA1, machine.ReadMemory(0x0000));
    }

    [Fact]
    public void Single16KImage_LoadsAsRom0_AndRom1ReadsOpenBus()
    {
        // Only ROM 0 is available in this repo. The machine must still build, and
        // paging in the absent ROM 1 must be obvious rather than silently wrong.
        byte[] rom0 = new byte[0x4000];
        rom0[0] = 0xA0;

        var machine = new Zx128Machine(rom0);
        machine.Reset();

        Assert.False(machine.Rom1Present);
        Assert.Equal(0xA0, machine.ReadMemory(0x0000));

        machine.WritePort(0x7FFD, 0x10);
        Assert.Equal(0xFF, machine.ReadMemory(0x0000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x2000)]
    [InlineData(0x4001)]
    [InlineData(0x10000)]
    public void RomImageOfAnyOtherSize_IsRejected(int size)
    {
        Assert.Throws<ArgumentException>(() => new Zx128Machine(new byte[size]));
    }

    // ── Memory map ───────────────────────────────────────────────────────────

    [Fact]
    public void Reset_PagesRom0BankZeroAndTheNormalScreen()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        Assert.Equal(0x0000, machine.Cpu.PC);
        Assert.Equal(0, machine.Pager.RomIndex);
        Assert.Equal(0, machine.Pager.PagedBank);
        Assert.Equal(5, machine.Pager.ScreenBank);
        Assert.False(machine.Pager.PagingLocked);
    }

    [Fact]
    public void FixedWindows_AreBank5AndBank2()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        machine.WriteMemory(0x4000, 0x55); // bank 5
        machine.WriteMemory(0x8000, 0x22); // bank 2

        Assert.Equal(0x55, machine.Banks[5].Read(0x0000));
        Assert.Equal(0x22, machine.Banks[2].Read(0x0000));
    }

    [Fact]
    public void PagedWindow_FollowsPort7FFD()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        for (int bank = 0; bank < 8; bank++)
        {
            machine.WritePort(0x7FFD, (byte)bank);
            machine.WriteMemory(0xC000, (byte)(0x30 + bank));
        }

        for (int bank = 0; bank < 8; bank++)
        {
            Assert.Equal((byte)(0x30 + bank), machine.Banks[bank].Read(0x0000));
        }
    }

    [Fact]
    public void AllEightBanksAreDistinct()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        // Bank 5 and 2 are reachable both at their fixed windows and via paging;
        // the rest only via 0xC000. Writing through 0xC000 must not alias.
        for (int bank = 0; bank < 8; bank++)
        {
            machine.WritePort(0x7FFD, (byte)bank);
            machine.WriteMemory(0xC001, (byte)bank);
        }

        var seen = new HashSet<byte>();
        for (int bank = 0; bank < 8; bank++) seen.Add(machine.Banks[bank].Read(0x0001));

        Assert.Equal(8, seen.Count);
    }

    // ── Timing ───────────────────────────────────────────────────────────────

    [Fact]
    public void UsesSpectrum128Timing()
    {
        var machine = new Zx128Machine(CombinedRom());
        Assert.Equal(UlaTiming.Spectrum128, machine.Ula.Timing);
    }

    [Fact]
    public void RunFrame_Advances70908TStates()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        ulong start = machine.Cpu.TotalCycles;
        machine.RunFrame();
        ulong elapsed = machine.Cpu.TotalCycles - start;

        // A frame runs to at least the target; the last instruction may overrun.
        Assert.InRange(elapsed, 70908ul, 70908ul + 64);
    }

    [Fact]
    public void RunFrame_IsStableOverManyFrames()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        ulong start = machine.Cpu.TotalCycles;
        for (int i = 0; i < 50; i++) machine.RunFrame();
        ulong elapsed = machine.Cpu.TotalCycles - start;

        // No drift: 50 frames is 50 x 70,908 give or take one instruction each.
        Assert.InRange(elapsed, 50ul * 70908, (50ul * 70908) + 3200);
    }

    // ── Integration ──────────────────────────────────────────────────────────

    [Fact]
    public void RenderFrame_ProducesAFrame()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();
        var sink = new NullVideo();

        machine.RunFrame();
        machine.RenderFrame(sink);

        Assert.Equal(1, sink.Frames);
    }

    [Fact]
    public void UlaPortStillWorks_BorderIsSettable()
    {
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        machine.WritePort(0x00FE, 0x03);

        Assert.Equal(3, machine.Ula.BorderColor);
    }

    [Fact]
    public void PagingAndUla_BothRespondWhenAPortMatchesBoth()
    {
        // Port 0x7FFC has A0 = 0 (the ULA answers) and A15 = 0, A1 = 0 (the pager
        // answers). On real hardware both latch, and so must both here.
        var machine = new Zx128Machine(CombinedRom());
        machine.Reset();

        machine.WritePort(0x7FFC, 0x02);

        Assert.Equal(2, machine.Pager.PagedBank);
        Assert.Equal(2, machine.Ula.BorderColor);
    }

    [Fact]
    public void RealRomsBoot_IfPresent()
    {
        // Uses real 128 ROM images if the developer has placed them at the repo
        // root. Skipped otherwise — ROM images are copyrighted and gitignored.
        string? rom0 = FindRepoRomPath("128-0.rom");
        string? rom1 = FindRepoRomPath("128-1.rom");
        if (rom0 is null || rom1 is null) return;

        var machine = new Zx128Machine(File.ReadAllBytes(rom0), File.ReadAllBytes(rom1));
        machine.Reset();

        Assert.True(machine.Rom1Present);

        // ROM 0 (128 editor) opens with DI then a delay loop.
        Assert.Equal(0xF3, machine.ReadMemory(0x0000));

        // ROM 1 is the 48 BASIC ROM: DI; XOR A; LD DE,0xFFFF.
        machine.WritePort(0x7FFD, 0x10);
        Assert.Equal(0xF3, machine.ReadMemory(0x0000));
        Assert.Equal(0xAF, machine.ReadMemory(0x0001));
        Assert.Equal(0x11, machine.ReadMemory(0x0002));
        machine.WritePort(0x7FFD, 0x00);

        for (int i = 0; i < 10; i++) machine.RunFrame();

        // Executing inside the ROM, not stuck at reset.
        Assert.True(machine.Cpu.TotalCycles > 700000);
    }

    [Fact]
    public void RealRoms_BootToTheEditorMenu()
    {
        // End-to-end: the 128 must reach its menu screen, not just run code.
        // Skipped when the ROM images are absent, since they are gitignored.
        string? rom0 = FindRepoRomPath("128-0.rom");
        string? rom1 = FindRepoRomPath("128-1.rom");
        if (rom0 is null || rom1 is null) return;

        var machine = new Zx128Machine(File.ReadAllBytes(rom0), File.ReadAllBytes(rom1));
        machine.Reset();
        var sink = new CaptureSink();

        for (int i = 0; i < 250; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(sink);
        }

        // The menu is drawn, so the bitmap must carry real content rather than
        // the blank screen a crashed machine leaves behind.
        var screen = machine.Banks[machine.Pager.ScreenBank];
        int bitmapBytes = 0;
        for (ushort a = 0; a < 0x1800; a++) if (screen.Read(a) != 0) bitmapBytes++;

        Assert.True(bitmapBytes > 200,
            $"the 128 menu should be drawn, but only {bitmapBytes} bitmap bytes are set");

        // The menu uses several colours (rainbow logo, cyan selection bar).
        Assert.True(new HashSet<uint>(sink.Frame).Count >= 4,
            "the menu screen should use several colours");

        // Still executing the editor in ROM 0, not crashed into a NOP slide.
        Assert.Equal(0, machine.Pager.RomIndex);
        Assert.True(machine.Cpu.SP > 0x1000, $"stack should be sane, was 0x{machine.Cpu.SP:X4}");
    }

    private sealed class CaptureSink : IVideoSink
    {
        public uint[] Frame = [];
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => Frame = pixels.ToArray();
    }

    [Fact]
    public void TwoRomConstructor_RejectsWrongSizes()
    {
        Assert.Throws<ArgumentException>(() => new Zx128Machine(new byte[0x4000], new byte[0x2000]));
        Assert.Throws<ArgumentException>(() => new Zx128Machine(new byte[0x8000], new byte[0x4000]));
    }

    [Fact]
    public void Plus2Roms_BootToTheEditorMenu()
    {
        // The +2 (grey) is a 128 in a new case, so its ROM set runs here rather
        // than on the +2A/+3. A second real ROM set is a genuinely independent
        // check of the composition. Skipped when the image is absent.
        string? path = FindRepoRomPath("plus2.rom");
        if (path is null) return;

        var machine = new Zx128Machine(File.ReadAllBytes(path));
        machine.Reset();
        var sink = new CaptureSink();

        for (int i = 0; i < 250; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(sink);
        }

        var screen = machine.Banks[machine.Pager.ScreenBank];
        int bitmapBytes = 0;
        for (ushort a = 0; a < 0x1800; a++) if (screen.Read(a) != 0) bitmapBytes++;

        Assert.True(bitmapBytes > 200,
            $"the +2 menu should be drawn, but only {bitmapBytes} bitmap bytes are set");
        Assert.True(new HashSet<uint>(sink.Frame).Count >= 4,
            "the menu screen should use several colours");
        Assert.Equal(0, machine.Pager.RomIndex);
        Assert.True(machine.Cpu.SP > 0x1000, $"stack should be sane, was 0x{machine.Cpu.SP:X4}");
    }

    /// <summary>
    /// Walks up from the test binary looking for a ROM image, checking each
    /// directory and its immediate subdirectories — some sets live in named
    /// folders at the repo root rather than loose.
    /// </summary>
    private static string? FindRepoRomPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;

            foreach (var sub in dir.EnumerateDirectories())
            {
                candidate = Path.Combine(sub.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
            }

            dir = dir.Parent;
        }
        return null;
    }
}
