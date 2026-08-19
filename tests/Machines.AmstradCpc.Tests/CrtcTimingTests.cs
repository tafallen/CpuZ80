using Xunit;
using Machines.AmstradCpc;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// Frame and line timing driven by the CRTC rather than by constants.
/// </summary>
/// <remarks>
/// The line period is R0+1 characters and a frame is (R4+1)(R9+1)+R5 lines, so
/// reprogramming any of them changes how long a frame takes. Raster splits and
/// overscan screens depend on that being followed; with the period fixed, a
/// program that reprograms the CRTC runs at the wrong rate while still looking
/// like it works.
/// </remarks>
public class CrtcTimingTests
{
    private static CpcMachine Build()
    {
        var machine = new CpcMachine(CpcBootTests.TestRom(), CpcBootTests.TestRom());
        machine.Reset();
        return machine;
    }

    private static void SetCrtc(CpcMachine machine, int register, byte value)
    {
        machine.WritePort(0xBC00, (byte)register);
        machine.WritePort(0xBD00, value);
    }

    private static ulong TimeOneFrame(CpcMachine machine)
    {
        ulong before = machine.Cpu.TotalCycles;
        machine.RunFrame();
        return machine.Cpu.TotalCycles - before;
    }

    [Fact]
    public void TheDefaultCrtcSetupGivesAStandardFrame()
    {
        // 64 characters a line, 39 rows of 8 lines: 312 scanlines of 256
        // T-states, which is 50 Hz at 4 MHz.
        var machine = Build();

        ulong elapsed = TimeOneFrame(machine);

        Assert.InRange(elapsed, (ulong)CpcMachine.FrameCycles, (ulong)CpcMachine.FrameCycles + 512);
        Assert.Equal(312, machine.Crtc.ScanlinesPerFrame);
    }

    [Fact]
    public void AWiderLineMakesTheFrameLonger()
    {
        var machine = Build();
        ulong standard = TimeOneFrame(machine);

        SetCrtc(machine, 0, 127);           // twice as many characters per line
        ulong wide = TimeOneFrame(machine);

        Assert.InRange(wide, standard * 2 - 1024, standard * 2 + 1024);
    }

    [Fact]
    public void FewerRowsMakeTheFrameShorter()
    {
        var machine = Build();
        ulong standard = TimeOneFrame(machine);

        SetCrtc(machine, 4, 18);            // 19 rows rather than 39
        ulong shorter = TimeOneFrame(machine);

        Assert.True(shorter < standard,
            $"halving the rows should shorten the frame, but it went from {standard} to {shorter}");
    }

    [Fact]
    public void TheVerticalAdjustAddsScanlines()
    {
        // R5 is how a CPC gets exactly 312 lines out of rows that do not divide
        // evenly into it — ignoring it makes every frame slightly wrong.
        var machine = Build();
        ulong before = TimeOneFrame(machine);

        SetCrtc(machine, 5, 6);
        ulong after = TimeOneFrame(machine);

        int lineLength = (machine.Crtc.HorizontalTotal + 1) * CpcMachine.CyclesPerCharacter;
        Assert.InRange(after - before, (ulong)(6 * lineLength) - 256, (ulong)(6 * lineLength) + 256);
    }

    [Fact]
    public void TallerCharacterRowsMakeTheFrameLonger()
    {
        var machine = Build();
        ulong eightLines = TimeOneFrame(machine);

        SetCrtc(machine, 9, 15);            // 16 scanlines per row instead of 8
        ulong sixteenLines = TimeOneFrame(machine);

        Assert.InRange(sixteenLines, eightLines * 2 - 2048, eightLines * 2 + 2048);
    }

    [Fact]
    public void FrameLengthDoesNotDriftOverManyFrames()
    {
        // The line target is carried rather than measured from the current cycle
        // count, so each line's last instruction overshooting does not
        // accumulate into a slow clock.
        var machine = Build();

        ulong before = machine.Cpu.TotalCycles;
        for (int i = 0; i < 50; i++) machine.RunFrame();
        ulong elapsed = machine.Cpu.TotalCycles - before;

        ulong expected = 50UL * CpcMachine.FrameCycles;
        Assert.InRange(elapsed, expected, expected + 2048);
    }

    // ── VSync ────────────────────────────────────────────────────────────────

    [Fact]
    public void VSyncPositionComesFromR7()
    {
        var machine = Build();

        SetCrtc(machine, 7, 30);
        Assert.Equal(30 * 8, machine.Crtc.VSyncStartScanline);

        SetCrtc(machine, 9, 15);
        Assert.Equal(30 * 16, machine.Crtc.VSyncStartScanline);
    }

    [Fact]
    public void AZeroVSyncWidthMeansSixteenLines()
    {
        // The 6845 treats a written zero as the maximum, which is easy to model
        // as "no VSync at all" and then wonder why the firmware never syncs.
        var machine = Build();

        SetCrtc(machine, 3, 0x00);
        Assert.Equal(16, machine.Crtc.VerticalSyncWidth);

        SetCrtc(machine, 3, 0x40);
        Assert.Equal(4, machine.Crtc.VerticalSyncWidth);
    }

    [Fact]
    public void VSyncIsSeenByTheFirmwareThroughPortB()
    {
        // Port B bit 0 is how the firmware waits for flyback. If it never goes
        // high the machine hangs waiting, which looks like a CPU fault.
        var machine = Build();

        machine.Ppi.VSync = true;
        Assert.Equal(1, machine.ReadPort(0xF500) & 0x01);

        machine.Ppi.VSync = false;
        Assert.Equal(0, machine.ReadPort(0xF500) & 0x01);
    }

    // ── Interrupts still land at the right rate ──────────────────────────────

    [Fact]
    public void InterruptsStillArriveAtRoughly300Hz()
    {
        // Six per 50 Hz frame. The interrupt counter is fed from HSyncs, so
        // deriving the line period from the CRTC must not change the rate for a
        // standard setup.
        var machine = Build();
        machine.Cpu.IFF1 = true;
        machine.Cpu.IM = 1;

        int interrupts = 0;
        machine.GateArray.InterruptRequested += () => interrupts++;

        machine.RunFrame();

        Assert.InRange(interrupts, 5, 7);
    }
}
