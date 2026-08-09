using Xunit;
using CpuZ80.TestSupport;
using Machines.Common;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// ZX Spectrum +2A/+3 motherboard composition: 8 RAM banks, 4 ROMs, +2A timing.
/// </summary>
public class Plus3MachineTests
{
    /// <summary>A 64K image with each 16K ROM stamped so the paged one is identifiable.</summary>
    private static byte[] CombinedRom()
    {
        byte[] image = new byte[0x10000];
        for (int i = 0; i < 4; i++) image[i * 0x4000] = (byte)(0xA0 + i);
        return image;
    }

    private sealed class NullVideo : IVideoSink
    {
        public int Frames;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => Frames++;
    }

    private sealed class CaptureSink : IVideoSink
    {
        public uint[] Frame = [];
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => Frame = pixels.ToArray();
    }

    // ── ROM loading ──────────────────────────────────────────────────────────

    [Fact]
    public void Combined64KImage_IsSplitIntoFourRoms()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        Assert.Equal(0xA0, machine.ReadMemory(0x0000));

        machine.WritePort(0x7FFD, 0x10);            // ROM select, low bit
        Assert.Equal(0xA1, machine.ReadMemory(0x0000));

        machine.WritePort(0x1FFD, 0x04);            // ROM select, high bit
        Assert.Equal(0xA3, machine.ReadMemory(0x0000));

        machine.WritePort(0x7FFD, 0x00);
        Assert.Equal(0xA2, machine.ReadMemory(0x0000));
    }

    [Fact]
    public void Constructors_RejectWrongSizes()
    {
        Assert.Throws<ArgumentException>(() => new Plus3Machine(new byte[0x8000]));
        Assert.Throws<ArgumentException>(() => new Plus3Machine([new byte[0x4000], new byte[0x4000]]));
        Assert.Throws<ArgumentException>(() =>
            new Plus3Machine([new byte[0x4000], new byte[0x4000], new byte[0x2000], new byte[0x4000]]));
    }

    // ── Composition ──────────────────────────────────────────────────────────

    [Fact]
    public void UsesPlus2ATiming_NotThe128s()
    {
        var machine = new Plus3Machine(CombinedRom());
        Assert.Equal(UlaTiming.Spectrum2A, machine.Ula.Timing);
        Assert.False(machine.Ula.Timing.ContendsIo);
    }

    [Fact]
    public void HasEightRamBanks()
    {
        var machine = new Plus3Machine(CombinedRom());
        Assert.Equal(8, machine.Banks.Length);
    }

    [Fact]
    public void RamIsAddressableThroughThePagedWindow()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        machine.WritePort(0x7FFD, 0x01);
        machine.WriteMemory(0xC000, 0x5A);

        machine.WritePort(0x7FFD, 0x02);
        machine.WriteMemory(0xC000, 0xA5);

        machine.WritePort(0x7FFD, 0x01);
        Assert.Equal(0x5A, machine.ReadMemory(0xC000));
        Assert.Equal(0x5A, machine.Banks[1].Read(0x0000));
        Assert.Equal(0xA5, machine.Banks[2].Read(0x0000));
    }

    [Fact]
    public void SpecialMode_MakesTheBottom16KWritableThroughTheCpu()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        machine.WritePort(0x1FFD, 0x01);            // all-RAM, config 0 → bank 0 at 0x0000
        machine.WriteMemory(0x0000, 0x3C);

        Assert.Equal(0x3C, machine.ReadMemory(0x0000));
        Assert.Equal(0x3C, machine.Banks[0].Read(0x0000));
    }

    [Fact]
    public void ShadowScreenBit_MovesTheDisplayToBank7()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        // Distinct content in each screen bank, so which one is displayed shows.
        for (ushort a = 0; a < 0x1800; a++) machine.Banks[5].Write(a, 0xFF);
        for (ushort a = 0x1800; a < 0x1B00; a++) machine.Banks[5].Write(a, 0x07);   // white on black
        for (ushort a = 0x1800; a < 0x1B00; a++) machine.Banks[7].Write(a, 0x07);

        var sink = new CaptureSink();
        machine.RunFrame();
        machine.RenderFrame(sink);
        int litNormal = sink.Frame.Count(p => p != sink.Frame[0]);

        machine.WritePort(0x7FFD, 0x08);            // shadow screen
        Assert.Equal(7, machine.Pager.ScreenBank);

        machine.RunFrame();
        machine.RenderFrame(sink);
        int litShadow = sink.Frame.Count(p => p != sink.Frame[0]);

        Assert.True(litNormal > 0, "bank 5 content should be visible");
        Assert.True(litShadow < litNormal, "switching to the empty bank 7 should blank the display");
    }

    [Fact]
    public void RunFrame_AdvancesByAFullFrameOfTStates()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        ulong before = machine.Cpu.TotalCycles;
        machine.RunFrame();
        ulong elapsed = machine.Cpu.TotalCycles - before;

        // Overshoots by at most the longest instruction.
        Assert.InRange(elapsed, (ulong)UlaTiming.Spectrum2A.FrameCycles, (ulong)UlaTiming.Spectrum2A.FrameCycles + 40);
    }

    [Fact]
    public void RenderFrame_SubmitsAFrame()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        var sink = new NullVideo();
        machine.RunFrame();
        machine.RenderFrame(sink);

        Assert.Equal(1, sink.Frames);
    }

    [Fact]
    public void AyRespondsOnItsPorts()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        machine.WritePort(0xFFFD, 0x07);            // select the mixer register
        machine.WritePort(0xBFFD, 0x38);

        // Reads come back on the select port; 0xBFFD is write-only.
        Assert.Equal(0x38, machine.ReadPort(0xFFFD));
    }

    [Fact]
    public void IoIsNotContended()
    {
        // The +2A/+3 gate array only contends when MREQ is active, so an I/O
        // access to a contended address costs the plain 4 T-states.
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();
        machine.RunFrame();     // put the ULA inside a frame

        ulong before = machine.Cpu.TotalCycles;
        machine.WritePort(0x7FFD, 0x01);
        Assert.Equal(before, machine.Cpu.TotalCycles);
    }

    // ── Disk drive ───────────────────────────────────────────────────────────

    [Fact]
    public void NoDriveByDefault_WhichMakesItAPlus2A()
    {
        var machine = new Plus3Machine(CombinedRom());
        machine.Reset();

        Assert.Null(machine.Fdc);

        // Nothing answers the controller's ports, so they float.
        Assert.Equal(0xFF, machine.ReadPort(0x2FFD));
    }

    [Fact]
    public void WithADrive_TheControllerAnswersItsPorts()
    {
        var machine = new Plus3Machine(CombinedRom(), diskDrive: true);
        machine.Reset();

        Assert.NotNull(machine.Fdc);
        Assert.Equal(machine.Fdc!.MainStatus, machine.ReadPort(0x2FFD));
        Assert.NotEqual(0xFF, machine.ReadPort(0x2FFD));
    }

    [Fact]
    public void MotorBitIsBit3Of1ffd_NotBit1()
    {
        // Bit 1 is ignored in normal mode; the motor is bit 3. Getting this
        // wrong leaves the drive permanently not-ready, and +3DOS just reports
        // a disk error.
        var machine = new Plus3Machine(CombinedRom(), diskDrive: true);
        machine.Reset();

        Assert.False(machine.Pager.MotorOn);

        machine.WritePort(0x1FFD, 0x02);        // bit 1
        Assert.False(machine.Pager.MotorOn);

        machine.WritePort(0x1FFD, 0x08);        // bit 3
        Assert.True(machine.Pager.MotorOn);
        Assert.True(machine.Fdc!.MotorOn);

        machine.WritePort(0x1FFD, 0x00);
        Assert.False(machine.Fdc.MotorOn);
    }

    [Fact]
    public void MotorStillRespondsWhilePagingIsLocked()
    {
        // The lock disables paging, not the whole port — a machine that froze
        // its drive motor by locking paging would be a strange design.
        var machine = new Plus3Machine(CombinedRom(), diskDrive: true);
        machine.Reset();

        machine.WritePort(0x7FFD, 0x20);        // lock paging
        Assert.True(machine.Pager.PagingLocked);

        machine.WritePort(0x1FFD, 0x08);
        Assert.True(machine.Fdc!.MotorOn);
    }

    [Fact]
    public void TheMotorBitDoesNotDisturbPaging()
    {
        var machine = new Plus3Machine(CombinedRom(), diskDrive: true);
        machine.Reset();

        machine.WritePort(0x1FFD, 0x04);        // ROM high bit
        machine.WritePort(0x1FFD, 0x0C);        // same, plus the motor

        Assert.True(machine.Pager.MotorOn);
        Assert.Equal(2, machine.Pager.RomIndex);
        Assert.False(machine.Pager.SpecialMode);
    }

    [Fact]
    public void ADiskCanBeReadThroughTheMachinesPorts()
    {
        var machine = new Plus3Machine(CombinedRom(), diskDrive: true);
        machine.Reset();
        machine.Fdc!.InsertDisk(0, new DiskImage(DskBuilder.Standard()));
        machine.WritePort(0x1FFD, 0x08);        // motor on

        // Read Data from track 0, sector 0x41.
        foreach (byte b in new byte[] { 0x46, 0x00, 0, 0, 0x41, 2, 0x49, 0x2A, 0xFF })
        {
            machine.WritePort(0x3FFD, b);
        }

        var data = new byte[DskBuilder.SectorSize];
        for (int i = 0; i < data.Length; i++) data[i] = machine.ReadPort(0x3FFD);

        Assert.All(data, b => Assert.Equal(DskBuilder.FillFor(0, 0x41), b));
    }

    // ── Real ROMs ────────────────────────────────────────────────────────────

    [Fact]
    public void RealRoms_HaveTheExpectedEntryPoints()
    {
        // Skipped when the ROM image is absent — it is copyrighted and gitignored.
        string? path = FindRepoRom("plus341.rom");
        if (path is null) return;

        var machine = new Plus3Machine(File.ReadAllBytes(path));
        machine.Reset();

        // ROM 0 is the +3 editor: DI; LD BC,...
        Assert.Equal(0xF3, machine.ReadMemory(0x0000));

        // ROM 3 is 48 BASIC: DI; XOR A; LD DE,0xFFFF.
        machine.WritePort(0x7FFD, 0x10);
        machine.WritePort(0x1FFD, 0x04);
        Assert.Equal(3, machine.Pager.RomIndex);
        Assert.Equal(0xF3, machine.ReadMemory(0x0000));
        Assert.Equal(0xAF, machine.ReadMemory(0x0001));
        Assert.Equal(0x11, machine.ReadMemory(0x0002));
    }

    [Fact]
    public void RealRoms_BootToTheEditorMenu()
    {
        // End-to-end: the +3 must reach its menu screen, not just run code.
        string? path = FindRepoRom("plus341.rom");
        if (path is null) return;

        var machine = new Plus3Machine(File.ReadAllBytes(path));
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
            $"the +3 menu should be drawn, but only {bitmapBytes} bitmap bytes are set");

        Assert.True(new HashSet<uint>(sink.Frame).Count >= 4,
            "the menu screen should use several colours");

        Assert.True(machine.Cpu.SP > 0x1000, $"stack should be sane, was 0x{machine.Cpu.SP:X4}");
    }

    [Fact]
    public void RealRoms_StillBootWithADriveFitted()
    {
        // Fitting the controller gives +3DOS something to talk to during
        // startup, and a controller that answers wrongly hangs it. The +2A path
        // booting proves nothing about the +3 path.
        string? path = FindRepoRom("plus341.rom");
        if (path is null) return;

        var machine = new Plus3Machine(File.ReadAllBytes(path), diskDrive: true);
        machine.Reset();
        machine.Fdc!.InsertDisk(0, new DiskImage(DskBuilder.Standard()));

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
            $"the +3 should still reach its menu with a drive fitted, only {bitmapBytes} bitmap bytes are set");
        Assert.True(machine.Cpu.SP > 0x1000, $"stack should be sane, was 0x{machine.Cpu.SP:X4}");
    }

    private static string? FindRepoRom(string fileName) => RomLocator.Find(fileName);
}
