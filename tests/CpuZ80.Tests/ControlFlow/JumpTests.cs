using Xunit;

namespace CpuZ80.Tests;

public class JumpTests : CpuFixture
{
    [Fact]
    public void JP_nn_SetsProgramCounter()
    {
        Load(0x0000, 0xC3, 0x34, 0x12); // JP $1234
        Step();
        Assert.Equal(0x1234, Cpu.PC);
        Assert.Equal(10UL, Cpu.TotalCycles);
    }

    [Fact]
    public void JR_e_PerformsRelativeJump()
    {
        Load(0x1000, 0x18, 0x05); // JR +5
        Step();
        Assert.Equal(0x1007, Cpu.PC); // $1000 + 2 (instr) + 5 (offset)
        Assert.Equal(12UL, Cpu.TotalCycles);
    }

    [Fact]
    public void JR_e_PerformsNegativeRelativeJump()
    {
        Load(0x1000, 0x18, 0xFE); // JR -2 ($FE is -2 in two's complement)
        Step();
        Assert.Equal(0x1000, Cpu.PC); // $1000 + 2 - 2
    }

    [Fact]
    public void JP_cc_nn_JumpsIfConditionMet()
    {
        Cpu.FlagZ = true;
        Load(0x0000, 0xCA, 0x00, 0x20); // JP Z, $2000
        Step();
        Assert.Equal(0x2000, Cpu.PC);
    }

    [Fact]
    public void JP_cc_nn_DoesNotJumpIfConditionNotMet()
    {
        Cpu.FlagZ = false;
        Load(0x0000, 0xCA, 0x00, 0x20); // JP Z, $2000
        Step();
        Assert.Equal(0x0003, Cpu.PC); // PC advanced by instruction size only
    }

    [Fact]
    public void CALL_nn_PushesReturnAddressAndJumps()
    {
        Cpu.SP = 0x4000;
        Load(0x1000, 0xCD, 0x00, 0x20); // CALL $2000
        Step();
        
        Assert.Equal(0x2000, Cpu.PC);
        Assert.Equal(0x3FFE, Cpu.SP);
        // Should have pushed PC+3 ($1003)
        Assert.Equal(0x10, Ram.Read(0x3FFF));
        Assert.Equal(0x03, Ram.Read(0x3FFE));
        Assert.Equal(17UL, Cpu.TotalCycles);
    }

    [Fact]
    public void RET_PopsReturnAddress()
    {
        Cpu.SP = 0x3FFE;
        Ram.Write(0x3FFE, 0x03);
        Ram.Write(0x3FFF, 0x10);
        Load(0x0000, 0xC9); // RET
        
        Step();
        
        Assert.Equal(0x1003, Cpu.PC);
        Assert.Equal(0x4000, Cpu.SP);
        Assert.Equal(10UL, Cpu.TotalCycles);
    }

    [Fact]
    public void JR_cc_e_CyclesVaryBasedOnCondition()
    {
        // JR NZ, e (taken)
        Cpu.FlagZ = false;
        Load(0x0000, 0x20, 0x05); 
        Step();
        Assert.Equal(12UL, Cpu.TotalCycles);

        // JR NZ, e (not taken)
        Cpu.FlagZ = true;
        Load(0x1000, 0x20, 0x05);
        var startCycles = Cpu.TotalCycles;
        Step();
        Assert.Equal(startCycles + 7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void CALL_cc_nn_TakenAndNotTaken()
    {
        Cpu.SP = 0x4000;
        Cpu.FlagC = true;
        
        // CALL C, $2000 (Taken)
        Load(0x1000, 0xDC, 0x00, 0x20); 
        Step();
        Assert.Equal(0x2000, Cpu.PC);
        Assert.Equal(0x3FFE, Cpu.SP);
        Assert.Equal(17UL, Cpu.TotalCycles);

        // CALL NC, $3000 (Not Taken)
        Load(0x2000, 0xD4, 0x00, 0x30);
        var startCycles = Cpu.TotalCycles;
        Step();
        Assert.Equal(0x2003, Cpu.PC);
        Assert.Equal(0x3FFE, Cpu.SP); // SP unchanged
        Assert.Equal(startCycles + 10UL, Cpu.TotalCycles);
    }

    [Fact]
    public void RET_cc_TakenAndNotTaken()
    {
        Cpu.SP = 0x3FFE;
        Ram.Write(0x3FFE, 0x03);
        Ram.Write(0x3FFF, 0x10);
        Cpu.FlagS = true; // Sign set (Minus)
        
        // RET M (Taken)
        Load(0x0000, 0xF8); 
        Step();
        Assert.Equal(0x1003, Cpu.PC);
        Assert.Equal(11UL, Cpu.TotalCycles);

        // RET P (Not Taken)
        Cpu.PC = 0x2000;
        Load(0x2000, 0xF0);
        var startCycles = Cpu.TotalCycles;
        Step();
        Assert.Equal(0x2001, Cpu.PC);
        Assert.Equal(startCycles + 5UL, Cpu.TotalCycles);
    }

    [Fact]
    public void CheckCondition_CoversAllFlags()
    {
        // NZ, Z covered by other tests
        // NC, C covered by other tests
        
        // PO (Parity Odd / Overflow Reset)
        Cpu.FlagPV = false;
        Load(0x0000, 0xE2, 0x34, 0x12); // JP PO, $1234
        Step();
        Assert.Equal(0x1234, Cpu.PC);

        // PE (Parity Even / Overflow Set)
        Cpu.FlagPV = true;
        Load(0x2000, 0xEA, 0x34, 0x12); // JP PE, $1234
        Step();
        Assert.Equal(0x1234, Cpu.PC);

        // P (Sign Positive)
        Cpu.FlagS = false;
        Load(0x3000, 0xF2, 0x34, 0x12); // JP P, $1234
        Step();
        Assert.Equal(0x1234, Cpu.PC);

        // M (Sign Minus)
        Cpu.FlagS = true;
        Load(0x4000, 0xFA, 0x34, 0x12); // JP M, $1234
        Step();
        Assert.Equal(0x1234, Cpu.PC);
    }
}
