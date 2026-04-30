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

    [Fact]
    public void LD_IX_plus_d_r_StoresToMemory()
    {
        Cpu.IX = 0x1000;
        Cpu.B = 0x55;
        Load(0x0000, 0xDD, 0x70, 0x05); // LD (IX+5), B
        Step();
        Assert.Equal(0x55, Ram.Read(0x1005));
    }

    [Fact]
    public void LD_r_IX_plus_d_LoadsFromMemory()
    {
        Cpu.IX = 0x2000;
        Ram.Write(0x2002, 0xAA);
        Load(0x0000, 0xDD, 0x46, 0x02); // LD B, (IX+2)
        Step();
        Assert.Equal(0xAA, Cpu.B);
    }

    [Fact]
    public void LD_r_r_WithIndexPrefix_AddsCycles()
    {
        Cpu.B = 0x11;
        Load(0x0000, 0xDD, 0x41); // LD B, C with DD prefix (invalid but adds cycles/fallback)
        var startCycles = Cpu.TotalCycles;
        Step();
        Assert.Equal(0, Cpu.B); // C was 0
        Assert.Equal(startCycles + 8UL, Cpu.TotalCycles); // 4 (LD) + 4 (Prefix)
    }

    [Fact]
    public void IndexedArithmetic_CoversAllTypes()
    {
        Cpu.IX = 0x8000;
        Ram.Write(0x8001, 0x05); // Data at $8001
        Cpu.A = 0x10;

        // ADC A, (IX+1)
        Cpu.FlagC = true;
        Load(0x0000, 0xDD, 0x8E, 0x01); 
        Step();
        Assert.Equal(0x16, Cpu.A);

        // SUB (IX+1)
        Cpu.PC = 0x1000;
        Load(0x1000, 0xDD, 0x96, 0x01);
        Step();
        Assert.Equal(0x11, Cpu.A);

        // SBC A, (IX+1)
        Cpu.PC = 0x2000;
        Cpu.FlagC = true;
        Load(0x2000, 0xDD, 0x9E, 0x01);
        Step();
        Assert.Equal(0x0B, Cpu.A);

        // AND (IX+1)
        Cpu.PC = 0x3000;
        Load(0x3000, 0xDD, 0xA6, 0x01);
        Step();
        Assert.Equal(0x01, Cpu.A);

        // XOR (IX+1)
        Cpu.PC = 0x4000;
        Load(0x4000, 0xDD, 0xAE, 0x01);
        Step();
        Assert.Equal(0x04, Cpu.A);

        // OR (IX+1)
        Cpu.PC = 0x5000;
        Load(0x5000, 0xDD, 0xB6, 0x01);
        Step();
        Assert.Equal(0x05, Cpu.A);

        // CP (IX+1)
        Cpu.PC = 0x6000;
        Load(0x6000, 0xDD, 0xBE, 0x01);
        Step();
        Assert.Equal(0x05, Cpu.A); // A unchanged
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void INC_IY_plus_d_IncrementsMemory()
    {
        Cpu.IY = 0x5000;
        Ram.Write(0x5002, 0x0F);
        Load(0x0000, 0xFD, 0x34, 0x02); // INC (IY+2)
        Step();
        Assert.Equal(0x10, Ram.Read(0x5002));
        Assert.True(Cpu.FlagH);
    }

    [Fact]
    public void DEC_IX_plus_d_DecrementsMemory()
    {
        Cpu.IX = 0x6000;
        Ram.Write(0x6002, 0x01);
        Load(0x0000, 0xDD, 0x35, 0x02); // DEC (IX+2)
        Step();
        Assert.Equal(0x00, Ram.Read(0x6002));
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void LD_IX_plus_d_n_LoadsImmediateToMemory()
    {
        Cpu.IX = 0x7000;
        Load(0x0000, 0xDD, 0x36, 0x01, 0x55); // LD (IX+1), $55
        Step();
        Assert.Equal(0x55, Ram.Read(0x7001));
    }

    [Fact]
    public void ADD_IX_SP_AddsToIX()
    {
        Cpu.IX = 0x1000;
        Cpu.SP = 0x0100;
        Load(0x0000, 0xDD, 0x39); // ADD IX, SP
        Step();
        Assert.Equal(0x1100, Cpu.IX);
    }

    [Fact]
    public void LD_IXH_n_LoadsHighByteOfIX()
    {
        Cpu.IX = 0x0000;
        Load(0x0000, 0xDD, 0x26, 0x42); // LD IXH, $42
        Step();
        Assert.Equal(0x4200, Cpu.IX);
    }

    [Fact]
    public void INC_IXL_IncrementsLowByteOfIX()
    {
        Cpu.IX = 0x00FF;
        Load(0x0000, 0xDD, 0x2C); // INC IXL
        Step();
        Assert.Equal(0x0000, Cpu.IX);
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void ADD_A_IXH_AddsHighByteOfIX()
    {
        Cpu.A = 0x10;
        Cpu.IX = 0x0500;
        Load(0x0000, 0xDD, 0x84); // ADD A, IXH
        Step();
        Assert.Equal(0x15, Cpu.A);
    }
}
