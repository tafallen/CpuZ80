using Xunit;

namespace CpuZ80.Tests;

public class ExtendedTests : CpuFixture
{
    [Fact]
    public void ADC_HL_BC_AddsWithCarry()
    {
        Cpu.HL = 0x1000;
        Cpu.BC = 0x0100;
        Cpu.FlagC = true;
        Load(0x0000, 0xED, 0x4A); // ADC HL, BC
        
        Step();
        
        Assert.Equal(0x1101, Cpu.HL);
        Assert.False(Cpu.FlagC);
        Assert.False(Cpu.FlagN);
    }

    [Fact]
    public void SBC_HL_DE_SubtractsWithCarry()
    {
        Cpu.HL = 0x1000;
        Cpu.DE = 0x0100;
        Cpu.FlagC = true;
        Load(0x0000, 0xED, 0x52); // SBC HL, DE
        
        Step();
        
        Assert.Equal(0x0EFF, Cpu.HL);
        Assert.True(Cpu.FlagN);
    }

    [Fact]
    public void LDI_CopiesByteAndUpdatesRegisters()
    {
        Cpu.HL = 0x1000; // Source
        Cpu.DE = 0x2000; // Destination
        Cpu.BC = 0x0002; // Counter
        Ram.Write(0x1000, 0xAA);
        
        Load(0x0000, 0xED, 0xA0); // LDI
        Step();
        
        Assert.Equal(0xAA, Ram.Read(0x2000));
        Assert.Equal(0x1001, Cpu.HL);
        Assert.Equal(0x2001, Cpu.DE);
        Assert.Equal(0x0001, Cpu.BC);
        Assert.True(Cpu.FlagPV); // BC != 0
        Assert.False(Cpu.FlagN);
        Assert.False(Cpu.FlagH);
        
        // Undocumented flags: res = A + val = 0x00 + 0xAA = 0xAA
        // Bit 3 of res is 1 (0x08), Bit 1 of res is 1 (0x02).
        // F bit 3 = res bit 3 = 1. F bit 5 = res bit 1 = 1.
        Assert.Equal(0x28, Cpu.F & 0x28);
    }

    [Fact]
    public void LDIR_CopiesBlockUntilBCZero()
    {
        Cpu.HL = 0x1000;
        Cpu.DE = 0x2000;
        Cpu.BC = 0x0003;
        Ram.Write(0x1000, 0x11);
        Ram.Write(0x1001, 0x22);
        Ram.Write(0x1002, 0x33);
        
        Load(0x0000, 0xED, 0xB0); // LDIR
        
        // LDIR is implemented as repeated LDI. In a cycle-accurate emulator, 
        // it re-executes by decrementing PC.
        while(Cpu.BC > 0) Step();
        
        Assert.Equal(0x11, Ram.Read(0x2000));
        Assert.Equal(0x22, Ram.Read(0x2001));
        Assert.Equal(0x33, Ram.Read(0x2002));
        Assert.Equal(0, Cpu.BC);
    }

    [Fact]
    public void LD_I_A_Transfer()
    {
        Cpu.A = 0x55;
        Load(0x0000, 0xED, 0x47); // LD I, A
        Step();
        // Since I is internal/private, we'll verify via LD A, I
        Cpu.A = 0x00;
        Load(0x1000, 0xED, 0x57); // LD A, I
        Cpu.PC = 0x1000;
        Step();
        Assert.Equal(0x55, Cpu.A);
    }

    [Fact]
    public void CPIR_SearchesMemoryForA()
    {
        Cpu.A = 0x55;
        Cpu.HL = 0x1000;
        Cpu.BC = 0x0003;
        Ram.Write(0x1000, 0x11);
        Ram.Write(0x1001, 0x55); // Found here
        Ram.Write(0x1002, 0x33);
        
        Load(0x0000, 0xED, 0xB1); // CPIR
        
        while(Cpu.BC > 0 && !Cpu.FlagZ) Step();
        
        Assert.Equal(0x1002, Cpu.HL);
        Assert.Equal(0x0001, Cpu.BC);
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void LDDR_CopiesBlockBackwards()
    {
        Cpu.HL = 0x1002;
        Cpu.DE = 0x2002;
        Cpu.BC = 0x0002;
        Ram.Write(0x1002, 0xAA);
        Ram.Write(0x1001, 0xBB);
        
        Load(0x0000, 0xED, 0xB8); // LDDR
        
        while(Cpu.BC > 0) Step();
        
        Assert.Equal(0xAA, Ram.Read(0x2002));
        Assert.Equal(0xBB, Ram.Read(0x2001));
        Assert.Equal(0, Cpu.BC);
    }

    [Fact]
    public void CPD_SearchesMemoryBackwards()
    {
        Cpu.A = 0x55;
        Cpu.HL = 0x1000;
        Cpu.BC = 0x0001;
        Ram.Write(0x1000, 0x55);
        Load(0x0000, 0xED, 0xA9); // CPD
        Step();
        Assert.Equal(0x0FFF, Cpu.HL);
        Assert.Equal(0, Cpu.BC);
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void CPDR_SearchesMemoryBackwardsUntilFound()
    {
        Cpu.A = 0x55;
        Cpu.HL = 0x1002;
        Cpu.BC = 0x0003;
        Ram.Write(0x1002, 0x11);
        Ram.Write(0x1001, 0x55);
        Ram.Write(0x1000, 0x33);
        Load(0x0000, 0xED, 0xB9); // CPDR
        while(Cpu.BC > 0 && !Cpu.FlagZ) Step();
        Assert.Equal(0x1000, Cpu.HL);
        Assert.True(Cpu.FlagZ);
    }

    [Fact]
    public void CPIR_TerminationOnBCZero()
    {
        Cpu.A = 0xFF; // Not in memory
        Cpu.HL = 0x1000;
        Cpu.BC = 0x0002;
        Ram.Write(0x1000, 0x11);
        Ram.Write(0x1001, 0x22);
        Load(0x0000, 0xED, 0xB1); // CPIR
        while(Cpu.BC > 0 && !Cpu.FlagZ) Step();
        Assert.Equal(0, Cpu.BC);
        Assert.False(Cpu.FlagZ);
    }

    [Fact]
    public void ADC_HL_BC_NoCarry()
    {
        Cpu.HL = 0x1000;
        Cpu.BC = 0x0100;
        Cpu.FlagC = false;
        Load(0x0000, 0xED, 0x4A); // ADC HL, BC
        Step();
        Assert.Equal(0x1100, Cpu.HL);
    }

    [Fact]
    public void SBC_HL_DE_NoCarry()
    {
        Cpu.HL = 0x1000;
        Cpu.DE = 0x0100;
        Cpu.FlagC = false;
        Load(0x0000, 0xED, 0x52); // SBC HL, DE
        Step();
        Assert.Equal(0x0F00, Cpu.HL);
    }
}
