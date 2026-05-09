using Xunit;

namespace CpuZ80.Tests;

public class CpuAccuracyTests : CpuFixture
{
    // ── EI one-instruction delay ──────────────────────────────────────────────

    [Fact]
    public void EI_DoesNotAcceptINT_UntilAfterNextInstruction()
    {
        Cpu.SP = 0x0200;
        Cpu.IFF1 = false;
        Cpu.IFF2 = false;
        // EI followed by NOP — INT should fire after the NOP, not before it
        Load(0x0000, 0xFB, 0x00); // EI, NOP
        Cpu.TriggerInt();
        Step(); // EI — INT must not fire here
        Assert.Equal(0x0001, Cpu.PC); // NOP not yet executed
        Step(); // NOP — INT must not fire during this step either
        Assert.Equal(0x0002, Cpu.PC); // NOP ran normally
        Step(); // now INT fires
        Assert.Equal(0x0038, Cpu.PC);
    }

    [Fact]
    public void EI_RETI_ExecutesRetiBefore_AcceptingINT()
    {
        // Classic interrupt handler ending: EI; RETI
        // INT must not re-enter before RETI returns
        Cpu.SP = 0x01FE;
        Ram.Write(0x01FE, 0x00);
        Ram.Write(0x01FF, 0x20); // return address 0x2000
        Cpu.IFF1 = false;
        Load(0x0000, 0xFB, 0xED, 0x4D); // EI; RETI
        Cpu.TriggerInt();
        Step(); // EI
        Assert.Equal(0x0001, Cpu.PC);
        Step(); // RETI — must execute fully, returning to 0x2000
        Assert.Equal(0x2000, Cpu.PC);
    }

    // ── Invalid ED opcodes are NOPs ───────────────────────────────────────────

    [Fact]
    public void InvalidED_Opcode_ActsAsNop()
    {
        Load(0x0000, 0xED, 0x00); // ED 00 — not a real instruction
        Step();
        Assert.Equal(0x0002, Cpu.PC);
        Assert.Equal(8UL, Cpu.TotalCycles);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x30)]
    [InlineData(0x38)]
    [InlineData(0x80)]
    [InlineData(0xC0)]
    [InlineData(0xFF)]
    public void InvalidED_Opcodes_DoNotThrow(byte edOpcode)
    {
        Load(0x0000, 0xED, edOpcode);
        var ex = Record.Exception(() => Step());
        Assert.Null(ex);
        Assert.Equal(0x0002, Cpu.PC);
    }

    // ── R register increments during HALT ────────────────────────────────────

    [Fact]
    public void HALT_IncrementsR_EachStep()
    {
        Load(0x0100, 0x76); // HALT
        Cpu.PC = 0x0100;
        Step(); // enter HALT — R incremented by Fetch() during first step
        byte rAfterEntry = Cpu.R;
        Step(); // halted step 1
        Assert.Equal((byte)(rAfterEntry + 1), Cpu.R);
        Step(); // halted step 2
        Assert.Equal((byte)(rAfterEntry + 2), Cpu.R);
    }

    [Fact]
    public void HALT_R_PreservesBit7()
    {
        Cpu.R = 0xFF; // bit 7 set, bits 0–6 = 0x7F (max — next increment wraps lower 7 bits to 0)
        Load(0x0100, 0x76);
        Cpu.PC = 0x0100;
        Step(); // HALT fetch increments R: lower 7 bits wrap to 0x00, bit 7 preserved → 0x80
        Step(); // halted step: R should go to 0x81
        Assert.Equal(0x81, Cpu.R);
        Assert.True((Cpu.R & 0x80) != 0); // bit 7 still set
    }
}
