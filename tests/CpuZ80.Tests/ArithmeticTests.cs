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
}
