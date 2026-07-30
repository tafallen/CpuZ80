using Xunit;
using CpuZ80.Core;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// ZX Spectrum ULA memory contention.
/// </summary>
/// <remarks>
/// The ULA and the CPU share the lower 16K of RAM (0x4000-0x7FFF). While the ULA
/// is drawing the visible picture it steals bus cycles, stalling any CPU access
/// to that bank. Contention applies only:
///   * to addresses 0x4000-0x7FFF (the upper 32K is unaffected),
///   * during visible scanlines 64-255 (T-states 14,336-57,343 of the frame),
///   * during the first 128 T-states of each 224 T-state line (the drawn part).
///
/// Within that window the delay follows a repeating 8-cycle pattern
/// 6,5,4,3,2,1,0,0.
///
/// These tests set Cpu.TotalCycles directly to place the machine at a chosen
/// point in the frame; _frameStartCycles is 0 after Reset, so TotalCycles is the
/// frame-relative T-state position.
/// </remarks>
public class ContentionTests
{
    private const int CyclesPerLine = 224;
    private const int VisibleStart = 64 * CyclesPerLine;   // 14,336
    private const int VisibleEnd = 256 * CyclesPerLine;    // 57,344
    private const ushort ContendedAddress = 0x4000;
    private const ushort UncontendedAddress = 0x8000;

    /// <summary>The ULA's published delay pattern, repeating every 8 T-states.</summary>
    private static readonly int[] ExpectedPattern = [6, 5, 4, 3, 2, 1, 0, 0];

    private static ZxSpectrumMachine MachineAt(ulong tState)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000]);
        machine.Reset();
        machine.Cpu.TotalCycles = tState;
        return machine;
    }

    /// <summary>T-states consumed by a 3-cycle read of <paramref name="address"/> at <paramref name="tState"/>.</summary>
    private static ulong ReadDuration(ulong tState, ushort address)
    {
        var machine = MachineAt(tState);
        ulong start = machine.Cpu.TotalCycles;
        machine.ReadMemory(address);
        return machine.Cpu.TotalCycles - start;
    }

    [Fact]
    public void ContendedRam_InVisibleArea_IsSlowerThanUncontended()
    {
        // Line 100, start of the drawn portion.
        ulong t = 100 * (ulong)CyclesPerLine;

        ulong contended = ReadDuration(t, ContendedAddress);
        ulong uncontended = ReadDuration(t, UncontendedAddress);

        Assert.True(contended > uncontended,
            $"contended ({contended}) should exceed uncontended ({uncontended})");
    }

    [Fact]
    public void UncontendedRam_IsNeverDelayed()
    {
        // The upper 32K is on a separate bus and always costs the bare 3 T-states.
        for (ulong line = 64; line < 256; line += 32)
        {
            ulong t = line * (ulong)CyclesPerLine;
            Assert.Equal(3ul, ReadDuration(t, UncontendedAddress));
        }
    }

    [Fact]
    public void ContendedRam_BeforeVisibleArea_IsNotDelayed()
    {
        // Top border: the ULA is not fetching yet.
        Assert.Equal(3ul, ReadDuration(0, ContendedAddress));
        Assert.Equal(3ul, ReadDuration(VisibleStart - 1, ContendedAddress));
    }

    [Fact]
    public void ContendedRam_AfterVisibleArea_IsNotDelayed()
    {
        // Bottom border / vertical blanking.
        Assert.Equal(3ul, ReadDuration(VisibleEnd, ContendedAddress));
        Assert.Equal(3ul, ReadDuration(VisibleEnd + 1000, ContendedAddress));
    }

    [Fact]
    public void ContendedRam_DuringHorizontalBlanking_IsNotDelayed()
    {
        // Only the first 128 T-states of each line are drawn; the remaining 96
        // are the right border and horizontal retrace.
        ulong lineStart = 100 * (ulong)CyclesPerLine;
        for (ulong offset = 128; offset < 224; offset += 16)
        {
            Assert.Equal(3ul, ReadDuration(lineStart + offset, ContendedAddress));
        }
    }

    [Fact]
    public void ContentionDelay_FollowsThe8CyclePattern()
    {
        // Sample the whole drawn portion of a visible line and confirm the delay
        // tracks 6,5,4,3,2,1,0,0 against the 3 T-state unwaited baseline.
        ulong lineStart = 100 * (ulong)CyclesPerLine;

        for (int offset = 0; offset < 128; offset++)
        {
            ulong duration = ReadDuration(lineStart + (ulong)offset, ContendedAddress);
            int expected = 3 + ExpectedPattern[offset % 8];

            Assert.True((ulong)expected == duration,
                $"line offset {offset}: expected {expected} T-states, got {duration}");
        }
    }

    [Fact]
    public void ContentionApplies_AtTheFirstAndLastVisibleTState()
    {
        // Boundary check: contention must switch on exactly at 14,336 and off at 57,344.
        Assert.Equal(3ul, ReadDuration(VisibleStart - 1, ContendedAddress));
        Assert.Equal((ulong)(3 + ExpectedPattern[VisibleStart % 8]), ReadDuration(VisibleStart, ContendedAddress));
        Assert.Equal(3ul, ReadDuration(VisibleEnd, ContendedAddress));
    }

    // ── Regression guards: contention must reach stack and block instructions ──
    // These fail if Push/Pop or the block instructions ever go back to calling
    // _bus.Read/_bus.Write directly and bypassing ICpuHost.

    /// <summary>Runs <paramref name="program"/> from <paramref name="origin"/> for a number of instructions.</summary>
    private static ulong RunFrom(ulong tState, ushort origin, int steps, params byte[] program)
    {
        var machine = MachineAt(tState);
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(origin - 0x4000 + i), program[i]);
        machine.Cpu.PC = origin;

        ulong start = machine.Cpu.TotalCycles;
        for (int i = 0; i < steps; i++) machine.Cpu.Step();
        return machine.Cpu.TotalCycles - start;
    }

    [Fact]
    public void CallAndRet_InContendedRam_AreContended()
    {
        // CALL 0x6100 ; (RET at 0x6100). Stack in contended RAM at 0x7000.
        byte[] program = [0x31, 0x00, 0x70, 0xCD, 0x00, 0x61];

        ulong visible = RunFrom(100 * (ulong)CyclesPerLine, 0x6000, 2, program);
        ulong blanking = RunFrom(0, 0x6000, 2, program);

        Assert.True(visible > blanking,
            $"CALL in the visible area ({visible}) should cost more than in blanking ({blanking})");
    }

    [Fact]
    public void PushAndPop_InContendedRam_AreContended()
    {
        // LD SP,0x7000 ; LD HL,0x1234 ; PUSH HL ; POP DE
        byte[] program = [0x31, 0x00, 0x70, 0x21, 0x34, 0x12, 0xE5, 0xD1];

        ulong visible = RunFrom(100 * (ulong)CyclesPerLine, 0x6000, 4, program);
        ulong blanking = RunFrom(0, 0x6000, 4, program);

        Assert.True(visible > blanking,
            $"PUSH/POP in the visible area ({visible}) should cost more than in blanking ({blanking})");
    }

    [Fact]
    public void Ldir_InContendedRam_IsContended()
    {
        // LD HL,0x4000 ; LD DE,0x5000 ; LD BC,0x0020 ; LDIR — entirely in contended RAM.
        byte[] program = [0x21, 0x00, 0x40, 0x11, 0x00, 0x50, 0x01, 0x20, 0x00, 0xED, 0xB0];

        ulong visible = RunFrom(100 * (ulong)CyclesPerLine, 0x6000, 40, program);
        ulong blanking = RunFrom(0, 0x6000, 40, program);

        Assert.True(visible > blanking,
            $"LDIR in the visible area ({visible}) should cost more than in blanking ({blanking})");
    }

    [Fact]
    public void Ldir_ToUncontendedRam_IsNotContendedOnTheWriteSide()
    {
        // Copying contended -> uncontended must cost less than contended -> contended,
        // because only the read side is stalled.
        byte[] toContended   = [0x21, 0x00, 0x40, 0x11, 0x00, 0x50, 0x01, 0x20, 0x00, 0xED, 0xB0];
        byte[] toUncontended = [0x21, 0x00, 0x40, 0x11, 0x00, 0x90, 0x01, 0x20, 0x00, 0xED, 0xB0];

        ulong t = 100 * (ulong)CyclesPerLine;
        ulong contendedTarget = RunFrom(t, 0x6000, 40, toContended);
        ulong uncontendedTarget = RunFrom(t, 0x6000, 40, toUncontended);

        Assert.True(uncontendedTarget < contendedTarget,
            $"writing to uncontended RAM ({uncontendedTarget}) should cost less than contended ({contendedTarget})");
    }
}
