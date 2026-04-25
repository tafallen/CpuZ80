using Xunit;

namespace CpuZ80.Tests;

public class StackTests : CpuFixture
{
    [Fact]
    public void PUSH_BC_DecrementsSP_And_WritesToMemory()
    {
        Cpu.SP = 0x1000;
        Cpu.BC = 0x1234;
        Load(0x0000, 0xC5); // PUSH BC
        
        Step();
        
        Assert.Equal(0x0FFE, Cpu.SP);
        Assert.Equal(0x12, Ram.Read(0x0FFF)); // High byte first
        Assert.Equal(0x34, Ram.Read(0x0FFE)); // Low byte second
        Assert.Equal(11UL, Cpu.TotalCycles);
    }

    [Fact]
    public void POP_DE_IncrementsSP_And_ReadsFromMemory()
    {
        Cpu.SP = 0x2000;
        Ram.Write(0x2000, 0x55); // Low
        Ram.Write(0x2001, 0xAA); // High
        Load(0x0000, 0xD1); // POP DE
        
        Step();
        
        Assert.Equal(0xAA55, Cpu.DE);
        Assert.Equal(0x2002, Cpu.SP);
        Assert.Equal(10UL, Cpu.TotalCycles);
    }

    [Fact]
    public void PUSH_POP_AF_PreservesAccumulatorAndFlags()
    {
        Cpu.SP = 0x4000;
        Cpu.A = 0x12;
        Cpu.FlagC = true;
        Cpu.FlagZ = true;
        
        Load(0x0000, 0xF5, 0xF1); // PUSH AF, POP AF
        
        Step(); // PUSH
        Cpu.A = 0x00;
        Cpu.F = 0x00;
        
        Step(); // POP
        Assert.Equal(0x12, Cpu.A);
        Assert.True(Cpu.FlagC);
        Assert.True(Cpu.FlagZ);
    }
}
