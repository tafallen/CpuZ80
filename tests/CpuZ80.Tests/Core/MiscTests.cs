using Xunit;

namespace CpuZ80.Tests;

public class MiscTests : CpuFixture
{
    [Fact]
    public void NOP_IncrementsPC_And_AddsCycles()
    {
        Load(0x0000, 0x00); // NOP
        
        var startPC = Cpu.PC;
        var startCycles = Cpu.TotalCycles;
        
        Step();
        
        Assert.Equal((ushort)(startPC + 1), Cpu.PC);
        Assert.Equal(startCycles + 4, Cpu.TotalCycles);
    }

    [Fact]
    public void EX_AF_AF_ExchangesAccumulatorAndFlags()
    {
        Cpu.A = 0x12;
        Cpu.FlagC = true;
        
        Load(0x0000, 0x08, 0x08); // EX AF, AF', EX AF, AF'
        
        Step(); // First swap
        Assert.Equal(0, Cpu.A);
        Assert.False(Cpu.FlagC);
        
        Step(); // Second swap
        Assert.Equal(0x12, Cpu.A);
        Assert.True(Cpu.FlagC);
    }

    [Fact]
    public void EXX_ExchangesRegisterPairs()
    {
        Cpu.BC = 0x1122;
        Cpu.DE = 0x3344;
        Cpu.HL = 0x5566;
        
        Load(0x0000, 0xD9, 0xD9); // EXX, EXX
        
        Step(); // First EXX
        Assert.Equal(0, Cpu.BC);
        Assert.Equal(0, Cpu.DE);
        Assert.Equal(0, Cpu.HL);
        
        Step(); // Second EXX
        Assert.Equal(0x1122, Cpu.BC);
        Assert.Equal(0x3344, Cpu.DE);
        Assert.Equal(0x5566, Cpu.HL);
    }

    [Fact]
    public void SCF_UpdatesUndocumentedFlagsFromA()
    {
        Cpu.A = 0x28; // Bits 3 and 5 set
        Cpu.F = 0x00;
        Load(0x0000, 0x37); // SCF
        Step();
        Assert.Equal(0x28, Cpu.F & 0x28);
        Assert.True(Cpu.FlagC);
    }

    [Fact]
    public void SetUndocumentedFlagsFromWZ_SetsCorrectBits()
    {
        Cpu.WZ = 0x2800; // Bits 11 and 13 set
        Cpu.F = 0x00;
        Cpu.SetUndocumentedFlagsFromWZ();
        Assert.Equal(0x28, Cpu.F);

        Cpu.WZ = 0xD7FF; // Bits 11 and 13 clear
        Cpu.F = 0xFF;
        Cpu.SetUndocumentedFlagsFromWZ();
        Assert.Equal(0xD7, Cpu.F); // 0xFF & ~0x28
    }
}
