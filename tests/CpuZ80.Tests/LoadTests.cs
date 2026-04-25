using Xunit;

namespace CpuZ80.Tests;

public class LoadTests : CpuFixture
{
    [Fact]
    public void LD_A_n_LoadsImmediateValue()
    {
        Load(0x0000, 0x3E, 0x42); // LD A, $42
        Step();
        Assert.Equal(0x42, Cpu.A);
        Assert.Equal(7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_B_n_LoadsImmediateValue()
    {
        Load(0x0000, 0x06, 0x12); // LD B, $12
        Step();
        Assert.Equal(0x12, Cpu.B);
        Assert.Equal(7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_HL_ptr_n_LoadsImmediateValueToMemory()
    {
        Cpu.HL = 0x1234;
        Load(0x0000, 0x36, 0xAA); // LD (HL), $AA
        Step();
        Assert.Equal(0xAA, Ram.Read(0x1234));
        Assert.Equal(10UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_r_r_CopiesValue()
    {
        Cpu.B = 0x55;
        Load(0x0000, 0x78); // LD A, B
        Step();
        Assert.Equal(0x55, Cpu.A);
        Assert.Equal(4UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_r_HL_ptr_LoadsFromMemory()
    {
        Cpu.HL = 0x8000;
        Ram.Write(0x8000, 0x99);
        Load(0x0000, 0x46); // LD B, (HL)
        Step();
        Assert.Equal(0x99, Cpu.B);
        Assert.Equal(7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_HL_ptr_r_StoresToMemory()
    {
        Cpu.HL = 0x9000;
        Cpu.C = 0x33;
        Load(0x0000, 0x71); // LD (HL), C
        Step();
        Assert.Equal(0x33, Ram.Read(0x9000));
        Assert.Equal(7UL, Cpu.TotalCycles);
    }

    [Fact]
    public void LD_AllRegisters_EnsuresCompleteCoverage()
    {
        // Testing D, E, H, L specifically
        Load(0x0000, 
            0x16, 0x11, // LD D, $11
            0x1E, 0x22, // LD E, $22
            0x26, 0x33, // LD H, $33
            0x2E, 0x44, // LD L, $44
            0x52,       // LD D, D (NOP-like, but hits Get/Set)
            0x5B,       // LD E, E
            0x64,       // LD H, H
            0x6D        // LD L, L
        );
        
        Step(8);
        
        Assert.Equal(0x11, Cpu.D);
        Assert.Equal(0x22, Cpu.E);
        Assert.Equal(0x33, Cpu.H);
        Assert.Equal(0x44, Cpu.L);
    }
}
