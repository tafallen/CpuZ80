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
        Assert.Equal(15UL, Cpu.TotalCycles); // CB instructions on (HL) take 15 cycles
    }
}
