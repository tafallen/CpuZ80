using Xunit;

namespace CpuZ80.Tests;

public class IndexTests : CpuFixture
{
    [Fact]
    public void LD_A_IX_plus_d_LoadsWithDisplacement()
    {
        Cpu.IX = 0x1000;
        Ram.Write(0x1005, 0x42);
        Load(0x0000, 0xDD, 0x7E, 0x05); // LD A, (IX+5)
        
        Step();
        
        Assert.Equal(0x42, Cpu.A);
        Assert.Equal(19UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_IY_nn_LoadsImmediate()
    {
        Load(0x0000, 0xFD, 0x21, 0x34, 0x12); // LD IY, $1234
        Step();
        Assert.Equal(0x1234, Cpu.IY);
    }

    [Fact]
    public void ADD_A_IX_plus_d_AddsWithDisplacement()
    {
        Cpu.A = 0x10;
        Cpu.IX = 0x2000;
        Ram.Write(0x1FFF, 0x05);
        Load(0x0000, 0xDD, 0x86, 0xFF); // ADD A, (IX-1)
        
        Step();
        
        Assert.Equal(0x15, Cpu.A);
        Assert.Equal(19UL, Cpu.TotalCycles);
    }

    [Fact]
    public void INC_IX_IncrementsRegister()
    {
        Cpu.IX = 0xFFFF;
        Load(0x0000, 0xDD, 0x23); // INC IX
        Step();
        Assert.Equal(0x0000, Cpu.IX);
        Assert.Equal(10UL, Cpu.TotalCycles);
    }
}
