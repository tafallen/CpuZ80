using Xunit;

namespace CpuZ80.Tests;

public class ArithmeticTests : CpuFixture
{
    [Fact]
    public void ADD_A_B_BasicAddition()
    {
        Cpu.A = 0x10;
        Cpu.B = 0x20;
        Load(0x0000, 0x80); // ADD A, B
        
        Step();
        
        Assert.Equal(0x30, Cpu.A);
        Assert.False(Cpu.FlagC);
        Assert.False(Cpu.FlagZ);
        Assert.False(Cpu.FlagS);
        Assert.False(Cpu.FlagH);
        Assert.False(Cpu.FlagPV);
        Assert.False(Cpu.FlagN);
    }

    [Fact]
    public void ADD_A_B_SetsCarry()
    {
        Cpu.A = 0xFF;
        Cpu.B = 0x01;
        Load(0x0000, 0x80); // ADD A, B
        
        Step();
        
        Assert.Equal(0x00, Cpu.A);
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagZ);
        Assert.False(Cpu.FlagS);
        Assert.True(Cpu.FlagH);
        Assert.False(Cpu.FlagPV);
        Assert.False(Cpu.FlagN);
    }

    [Fact]
    public void ADD_A_B_SetsOverflow()
    {
        Cpu.A = 0x7F; // 127
        Cpu.B = 0x01; // 1
        Load(0x0000, 0x80); // ADD A, B
        
        Step();
        
        Assert.Equal(0x80, Cpu.A); // -128
        Assert.True(Cpu.FlagPV); // Overflow set
        Assert.True(Cpu.FlagS);   // Sign set
        Assert.True(Cpu.FlagH);   // Half-carry from bit 3
    }

    [Fact]
    public void ADC_A_B_BasicAdditionWithCarry()
    {
        Cpu.A = 0x10;
        Cpu.B = 0x20;
        Cpu.FlagC = true;
        Load(0x0000, 0x88); // ADC A, B
        
        Step();
        
        Assert.Equal(0x31, Cpu.A);
        Assert.False(Cpu.FlagC);
    }

    [Fact]
    public void ADC_A_B_SetsCarryFromCarry()
    {
        Cpu.A = 0xFF;
        Cpu.B = 0x00;
        Cpu.FlagC = true;
        Load(0x0000, 0x88); // ADC A, B
        
        Step();
        
        Assert.Equal(0x00, Cpu.A);
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void ADD_A_HL_ptr_AddsFromMemory()
    {
        Cpu.A = 0x10;
        Cpu.HL = 0x8000;
        Ram.Write(0x8000, 0x05);
        Load(0x0000, 0x86); // ADD A, (HL)
        
        Step();
        
        Assert.Equal(0x15, Cpu.A);
        Assert.Equal(7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void ADC_A_HL_ptr_AddsFromMemoryWithCarry()
    {
        Cpu.A = 0x10;
        Cpu.HL = 0x8000;
        Cpu.FlagC = true;
        Ram.Write(0x8000, 0x05);
        Load(0x0000, 0x8E); // ADC A, (HL)
        
        Step();
        
        Assert.Equal(0x16, Cpu.A);
        Assert.Equal(7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void INC_B_IncrementsAndSetsFlags()
    {
        Cpu.B = 0x0F;
        Cpu.FlagC = true; // Carry should remain unchanged
        Load(0x0000, 0x04); // INC B
        
        Step();
        
        Assert.Equal(0x10, Cpu.B);
        Assert.True(Cpu.FlagH); // Half-carry from bit 3 to 4
        Assert.False(Cpu.FlagZ);
        Assert.False(Cpu.FlagN);
        Assert.True(Cpu.FlagC); // Still true
    }

    [Fact]
    public void INC_B_SetsOverflow()
    {
        Cpu.B = 0x7F;
        Load(0x0000, 0x04); // INC B
        
        Step();
        
        Assert.Equal(0x80, Cpu.B);
        Assert.True(Cpu.FlagPV); // Overflow
        Assert.True(Cpu.FlagS);   // Negative
    }

    [Fact]
    public void DEC_HL_ptr_DecrementsMemory()
    {
        Cpu.HL = 0x1000;
        Ram.Write(0x1000, 0x01);
        Load(0x0000, 0x35); // DEC (HL)
        
        Step();
        
        Assert.Equal(0x00, Ram.Read(0x1000));
        Assert.True(Cpu.FlagZ);
        Assert.True(Cpu.FlagN);
        Assert.False(Cpu.FlagH);
        Assert.Equal(11UL, Cpu.TotalCycles);
    }

    [Fact]
    public void AND_B_PerformsLogicalAnd()
    {
        Cpu.A = 0xAA; // 1010 1010
        Cpu.B = 0x0F; // 0000 1111
        Load(0x0000, 0xA0); // AND B
        
        Step();
        
        Assert.Equal(0x0A, Cpu.A);
        Assert.True(Cpu.FlagH); // AND always sets H
        Assert.False(Cpu.FlagC); // AND always clears C
        Assert.False(Cpu.FlagN); // AND always clears N
        Assert.True(Cpu.FlagPV); // Parity of 0x0A (2 bits set) is even -> PV=1
    }

    [Fact]
    public void XOR_C_PerformsLogicalXor()
    {
        Cpu.A = 0xAA;
        Cpu.C = 0x55;
        Load(0x0000, 0xA9); // XOR C
        
        Step();
        
        Assert.Equal(0xFF, Cpu.A);
        Assert.False(Cpu.FlagH); // XOR/OR always clear H
        Assert.False(Cpu.FlagC);
        Assert.False(Cpu.FlagN);
        Assert.True(Cpu.FlagPV); // Parity of 0xFF (8 bits set) is even -> PV=1
        Assert.True(Cpu.FlagS);
    }

    [Fact]
    public void CP_n_ComparesAndSetsFlags()
    {
        Cpu.A = 0x10;
        Load(0x0000, 0xFE, 0x10); // CP $10
        
        Step();
        
        Assert.Equal(0x10, Cpu.A); // A unchanged
        Assert.True(Cpu.FlagZ);
        Assert.True(Cpu.FlagN); // CP is a subtraction
        Assert.False(Cpu.FlagC);
    }

    [Fact]
    public void SUB_B_SubtractsValue()
    {
        Cpu.A = 0x10;
        Cpu.B = 0x01;
        Load(0x0000, 0x90); // SUB B
        
        Step();
        
        Assert.Equal(0x0F, Cpu.A);
        Assert.True(Cpu.FlagN);
        Assert.False(Cpu.FlagC);
    }

    [Fact]
    public void SBC_A_B_SubtractsWithCarry()
    {
        Cpu.A = 0x10;
        Cpu.B = 0x01;
        Cpu.FlagC = true;
        Load(0x0000, 0x98); // SBC A, B
        
        Step();
        
        Assert.Equal(0x0E, Cpu.A);
        Assert.True(Cpu.FlagN);
        Assert.False(Cpu.FlagC);
    }

    [Fact]
    public void SUB_A_SetsCarry()
    {
        Cpu.A = 0x00;
        Cpu.B = 0x01;
        Load(0x0000, 0x90); // SUB B
        
        Step();
        
        Assert.Equal(0xFF, Cpu.A);
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagH);
    }

    [Fact]
    public void OR_B_PerformsLogicalOr()
    {
        Cpu.A = 0x10;
        Cpu.B = 0x01;
        Load(0x0000, 0xB0); // OR B
        
        Step();
        
        Assert.Equal(0x11, Cpu.A);
        Assert.False(Cpu.FlagC);
        Assert.False(Cpu.FlagH);
        Assert.False(Cpu.FlagN);
        Assert.True(Cpu.FlagPV); // Parity of 0x11 (2 bits) is even
    }

    [Fact]
    public void ADD_HL_BC_Basic16BitAddition()
    {
        Cpu.HL = 0x1000;
        Cpu.BC = 0x0200;
        Cpu.FlagZ = true; // Should remain unchanged
        Load(0x0000, 0x09); // ADD HL, BC
        
        Step();
        
        Assert.Equal(0x1200, Cpu.HL);
        Assert.False(Cpu.FlagC);
        Assert.False(Cpu.FlagN);
        Assert.True(Cpu.FlagZ); // Unchanged
        Assert.Equal(11UL, Cpu.TotalCycles);
    }

    [Fact]
    public void ADD_HL_HL_SetsCarryAndHalfCarry()
    {
        Cpu.HL = 0x8800; // Bit 15 and bit 11 set
        Load(0x0000, 0x29); // ADD HL, HL
        
        Step();
        
        Assert.Equal(0x1000, Cpu.HL);
        Assert.True(Cpu.FlagC); // Carry from bit 15
        Assert.True(Cpu.FlagH); // Half-carry from bit 11
        Assert.False(Cpu.FlagN);
    }
}
