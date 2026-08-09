using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Hardware;

/// <summary>
/// The Amstrad CPC's 4 T-state memory access alignment.
/// </summary>
/// <remarks>
/// The Gate Array holds the Z80's READY line so no access completes off a
/// microsecond boundary, which makes every instruction take a multiple of 4
/// T-states. See docs/amstrad-cpc.md §7.
///
/// The flag defaults off and every other machine in this repo depends on that,
/// so half these tests exist to prove nothing changed.
/// </remarks>
public class FourTStateAlignmentTests : CpuFixture
{
    private ulong TimeOf(params byte[] program)
    {
        Load(0x0000, program);
        ulong before = Cpu.TotalCycles;
        Cpu.Step();
        return Cpu.TotalCycles - before;
    }

    // ── Default behaviour is unchanged ───────────────────────────────────────

    [Fact]
    public void AlignmentIsOffByDefault()
    {
        Assert.False(Cpu.AlignInstructionsTo4TStates);
    }

    [Theory]
    [InlineData(4, new byte[] { 0x00 })]                    // NOP
    [InlineData(7, new byte[] { 0x3E, 0x42 })]              // LD A,n
    [InlineData(7, new byte[] { 0x7E })]                    // LD A,(HL)
    [InlineData(10, new byte[] { 0x01, 0x34, 0x12 })]       // LD BC,nn
    [InlineData(13, new byte[] { 0x32, 0x00, 0x40 })]       // LD (nn),A
    public void WithoutAlignment_TimingsAreTheDocumentedZ80Values(int expected, byte[] program)
    {
        Assert.Equal((ulong)expected, TimeOf(program));
    }

    // ── With alignment ───────────────────────────────────────────────────────

    [Theory]
    // A documented 7 T-state instruction takes 8 on a CPC; 13 takes 16.
    [InlineData(4, 4, new byte[] { 0x00 })]                     // NOP: already aligned
    [InlineData(7, 8, new byte[] { 0x3E, 0x42 })]               // LD A,n
    [InlineData(7, 8, new byte[] { 0x7E })]                     // LD A,(HL)
    [InlineData(10, 12, new byte[] { 0x01, 0x34, 0x12 })]       // LD BC,nn
    [InlineData(13, 16, new byte[] { 0x32, 0x00, 0x40 })]       // LD (nn),A
    [InlineData(11, 12, new byte[] { 0xC5 })]                   // PUSH BC
    public void WithAlignment_TimingsRoundUpToAMultipleOfFour(
        int documented, int aligned, byte[] program)
    {
        // The documented value is asserted by the unaligned theory above; it is
        // repeated here so the pair can be read together.
        Assert.Equal(aligned, (documented + 3) / 4 * 4);

        Cpu.AlignInstructionsTo4TStates = true;

        Assert.Equal((ulong)aligned, TimeOf(program));
    }

    [Fact]
    public void EveryInstructionEndsOnABoundary()
    {
        // The property that matters is not any single instruction's length but
        // that the cycle counter is never off a boundary between instructions.
        Cpu.AlignInstructionsTo4TStates = true;

        Load(0x0000,
            0x00,                     // NOP        4
            0x3E, 0x42,               // LD A,n     7 -> 8
            0x01, 0x34, 0x12,         // LD BC,nn  10 -> 12
            0x7E,                     // LD A,(HL)  7 -> 8
            0xC5,                     // PUSH BC   11 -> 12
            0x32, 0x00, 0x40);        // LD (nn),A 13 -> 16

        for (int i = 0; i < 6; i++)
        {
            Cpu.Step();
            Assert.Equal(0ul, Cpu.TotalCycles % 4);
        }
    }

    [Fact]
    public void AlignmentDoesNotAccumulateDrift()
    {
        // Padding must be measured against an absolute boundary. Padding a
        // fixed amount per instruction, or measuring relative to where the
        // instruction started, would drift.
        Cpu.AlignInstructionsTo4TStates = true;

        // LD A,n twice: 7 T-states each, so 8 then 16 if alignment is absolute.
        Load(0x0000, 0x3E, 0x42, 0x3E, 0x42);

        Cpu.Step();
        Assert.Equal(8ul, Cpu.TotalCycles);

        Cpu.Step();
        Assert.Equal(16ul, Cpu.TotalCycles);
    }

    [Fact]
    public void AlignedInstructionsAreNotPadded()
    {
        // Rounding up must be a no-op when the instruction already ends on a
        // boundary — adding 4 unconditionally would make every NOP cost 8.
        Cpu.AlignInstructionsTo4TStates = true;

        Load(0x0000, 0x00, 0x00, 0x00, 0x00);
        for (int i = 0; i < 4; i++) Cpu.Step();

        Assert.Equal(16ul, Cpu.TotalCycles);
    }

    [Fact]
    public void HaltIsAligned()
    {
        Cpu.AlignInstructionsTo4TStates = true;

        Load(0x0000, 0x76);           // HALT
        Cpu.Step();
        ulong afterHalt = Cpu.TotalCycles;

        Cpu.Step();                   // halted: keeps ticking
        Assert.True(Cpu.IsHalted);
        Assert.Equal(0ul, Cpu.TotalCycles % 4);
        Assert.True(Cpu.TotalCycles > afterHalt);
    }

    [Fact]
    public void WaitStatesAreStillHonouredAndThenAligned()
    {
        // Contention and alignment are different uses of the same mechanism, and
        // a machine could in principle want both. Wait states are added first,
        // then the total is rounded up.
        var cpu = new Cpu(Ram, null, new WaitInjectingHost(2))
        {
            AlignInstructionsTo4TStates = true,
        };

        Ram.Load(0x0000, [0x00]);     // NOP: 4 T-states, plus 2 wait = 6 -> 8
        cpu.PC = 0x0000;
        cpu.Step();

        Assert.Equal(8ul, cpu.TotalCycles);
    }

    private sealed class WaitInjectingHost(int waits) : ICpuHost
    {
        public void OnMemoryAccess(ushort address, Cpu cpu) => cpu.WaitCycles += waits;
        public void OnPortAccess(ushort port, Cpu cpu) { }
    }
}
