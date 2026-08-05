using Xunit;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// Paging driven the way the ROM actually drives it — executed Z80 code, not
/// direct calls to the pager.
/// </summary>
/// <remarks>
/// The 128 ROM's ROM-switching trampoline uses <c>LD BC,0x7FFD : OUT (C),A</c>,
/// so the port address arrives on BC rather than as an immediate. The pager's
/// own tests call Out() directly and would not catch a break in that path.
/// </remarks>
public class Zx128PagingViaCpuTests
{
    private static Zx128Machine Machine(params byte[] program)
    {
        var m = new Zx128Machine(new byte[0x8000]);
        m.Reset();
        for (int i = 0; i < program.Length; i++)
            m.Banks[2].Write((ushort)i, program[i]);   // bank 2 is fixed at 0x8000
        m.Cpu.PC = 0x8000;
        return m;
    }

    [Fact]
    public void OutCA_ToPort7FFD_PagesTheBank()
    {
        // LD A,3 : LD BC,0x7FFD : OUT (C),A : HALT
        var m = Machine(0x3E, 0x03, 0x01, 0xFD, 0x7F, 0xED, 0x79, 0x76);

        for (int i = 0; i < 4; i++) m.Step();

        Assert.Equal(3, m.Pager.PagedBank);
    }

    [Fact]
    public void OutCA_SelectsRomAndScreen()
    {
        // LD A,0x18 (ROM 1, shadow screen) : LD BC,0x7FFD : OUT (C),A
        var m = Machine(0x3E, 0x18, 0x01, 0xFD, 0x7F, 0xED, 0x79, 0x76);

        for (int i = 0; i < 4; i++) m.Step();

        Assert.Equal(1, m.Pager.RomIndex);
        Assert.Equal(7, m.Pager.ScreenBank);
    }

    [Fact]
    public void OutNA_ToPort7FFD_AlsoPages()
    {
        // The other encoding: OUT (n),A puts A on the high address byte, so
        // LD A,0x7F : OUT (0xFD),A gives port 0x7FFD.
        var m = Machine(0x3E, 0x7F, 0xD3, 0xFD, 0x76);

        for (int i = 0; i < 2; i++) m.Step();

        // A = 0x7F -> bits 0-2 = 7
        Assert.Equal(7, m.Pager.PagedBank);
    }

    [Fact]
    public void LdSpNn_ThenLdSpHl_RoundTripsThroughMemory()
    {
        // The trampoline swaps SP through memory:
        //   LD (nn),SP : LD SP,HL
        // Both must work for the ROM switch to return to the right stack.
        // LD SP,0xFF58 : LD HL,0x5BFF : LD (0x9000),SP : LD SP,HL : HALT
        var m = Machine(
            0x31, 0x58, 0xFF,
            0x21, 0xFF, 0x5B,
            0xED, 0x73, 0x00, 0x90,
            0xF9,
            0x76);

        for (int i = 0; i < 4; i++) m.Step();

        Assert.Equal(0x5BFF, m.Cpu.SP);
        Assert.Equal(0x58, m.ReadMemory(0x9000));
        Assert.Equal(0xFF, m.ReadMemory(0x9001));
    }

    [Fact]
    public void LdSpFromMemory_ReadsBackWhatWasStored()
    {
        // LD (0x9000),SP then LD SP,(0x9000) must round-trip.
        var m = Machine(
            0x31, 0x58, 0xFF,
            0xED, 0x73, 0x00, 0x90,
            0x31, 0x00, 0x00,
            0xED, 0x7B, 0x00, 0x90,
            0x76);

        for (int i = 0; i < 4; i++) m.Step();

        Assert.Equal(0xFF58, m.Cpu.SP);
    }
}
