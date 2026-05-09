using Xunit;

namespace CpuZ80.Tests;

public class BitwiseTests : CpuFixture
{
    [Fact]
    public void RLC_B_RotatesLeftCircular()
    {
        Cpu.B = 0x81; // 1000 0001
        Load(0x0000, 0xCB, 0x00); // CB 00: RLC B
        
        Step();
        
        Assert.Equal(0x03, Cpu.B); // 0000 0011
        Assert.True(Cpu.FlagC); // Bit 7 moved to carry
        Assert.False(Cpu.FlagN);
        Assert.False(Cpu.FlagH);
    }

    [Fact]
    public void RL_C_RotatesLeftThroughCarry()
    {
        Cpu.C = 0x80;
        Cpu.FlagC = false;
        Load(0x0000, 0xCB, 0x11); // CB 11: RL C
        
        Step();
        
        Assert.Equal(0x00, Cpu.C);
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagZ);

        // Rotate again with carry set
        Cpu.PC = 0x1000;
        Load(0x1000, 0xCB, 0x11);
        Step();
        Assert.Equal(0x01, Cpu.C); // Carry moved into bit 0
        Assert.False(Cpu.FlagC);
    }

    [Fact]
    public void BIT_b_r_TestsSpecificBit()
    {
        Cpu.B = 0x08; // Bit 3 set
        
        // Test bit 3 (should be Z=0)
        Load(0x0000, 0xCB, 0x58); // BIT 3, B
        Step();
        Assert.False(Cpu.FlagZ);
        Assert.True(Cpu.FlagH); // BIT always sets H
        Assert.False(Cpu.FlagN);

        // Test bit 2 (should be Z=1)
        Load(0x1000, 0xCB, 0x50); // BIT 2, B
        Step();
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void SET_RES_ManipulateBits()
    {
        Cpu.D = 0x00;
        
        // SET 7, D
        Load(0x0000, 0xCB, 0xFA); 
        Step();
        Assert.Equal(0x80, Cpu.D);

        // RES 7, D
        Load(0x1000, 0xCB, 0xBA);
        Step();
        Assert.Equal(0x00, Cpu.D);
    }

    [Fact]
    public void SRL_HL_ptr_ShiftsMemoryRight()
    {
        Cpu.HL = 0x8000;
        Ram.Write(0x8000, 0x01);
        Load(0x0000, 0xCB, 0x3E); // SRL (HL)
        
        Step();
        
        Assert.Equal(0x00, Ram.Read(0x8000));
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagZ);
        Assert.Equal(15UL, Cpu.TotalCycles); 
    }

    [Fact]
    public void RRC_B_RotatesRightCircular()
    {
        Cpu.B = 0x01;
        Load(0x0000, 0xCB, 0x08); // RRC B
        Step();
        Assert.Equal(0x80, Cpu.B);
        Assert.True(Cpu.FlagC);
    }

    [Fact]
    public void RR_C_RotatesRightThroughCarry()
    {
        Cpu.C = 0x01;
        Cpu.FlagC = false;
        Load(0x0000, 0xCB, 0x19); // RR C
        Step();
        Assert.Equal(0x00, Cpu.C);
        Assert.True(Cpu.FlagC);
        
        Load(0x1000, 0xCB, 0x19); // RR C with Carry set
        Cpu.FlagC = true;
        Cpu.PC = 0x1000;
        Step();
        Assert.Equal(0x80, Cpu.C);
        Assert.False(Cpu.FlagC);
    }

    [Fact]
    public void SLA_B_ShiftsLeftArithmetic()
    {
        Cpu.B = 0x80;
        Load(0x0000, 0xCB, 0x20); // SLA B
        Step();
        Assert.Equal(0x00, Cpu.B);
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void SRA_B_ShiftsRightArithmetic()
    {
        Cpu.B = 0x81;
        Load(0x0000, 0xCB, 0x28); // SRA B
        Step();
        Assert.Equal(0xC0, Cpu.B); // Sign bit preserved
        Assert.True(Cpu.FlagC);
    }

    [Fact]
    public void SLL_B_ShiftsLeftLogicalUndocumented()
    {
        Cpu.B = 0x80;
        Load(0x0000, 0xCB, 0x30); // SLL B
        Step();
        Assert.Equal(0x01, Cpu.B); // Bit 0 becomes 1
        Assert.True(Cpu.FlagC);
    }

    [Fact]
    public void BIT_HL_ptr_UndocumentedFlags_LeakFromHL()
    {
        // For BIT n, (HL), bits 3 and 5 of flags are leaked from bits 11 and 13 of WZ (which is HL)
        Cpu.HL = 0x2800; // Bits 11 and 13 set
        Ram.Write(0x2800, 0x00);
        Load(0x0000, 0xCB, 0x46); // BIT 0, (HL)
        
        Step();
        
        // Flag bit 3 (value 8) and bit 5 (value 32) should be set because of WZ=0x2800
        Assert.Equal(0x28, Cpu.F & 0x28);
        
        Cpu.HL = 0x0000;
        Ram.Write(0x0000, 0x00);
        Load(0x1000, 0xCB, 0x46); // BIT 0, (HL)
        Cpu.PC = 0x1000;
        Step();
        Assert.Equal(0x00, Cpu.F & 0x28);
    }
}
