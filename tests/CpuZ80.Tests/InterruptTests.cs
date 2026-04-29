using Xunit;

namespace CpuZ80.Tests;

public class InterruptTests : CpuFixture
{
    // ── NMI ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NMI_JumpsTo0066_PushesReturnAddress()
    {
        Cpu.SP = 0x0200;
        Cpu.PC = 0x1234;
        Load(0x1234, 0x00); // NOP at return address
        Cpu.TriggerNmi();
        Step();
        Assert.Equal(0x0066, Cpu.PC);
        Assert.Equal(0x01FE, Cpu.SP);
        Assert.Equal(0x12, Ram.Read(0x01FF));
        Assert.Equal(0x34, Ram.Read(0x01FE)); // PC at point of NMI (before fetch)
        Assert.Equal(11UL, Cpu.TotalCycles);
    }

    [Fact]
    public void NMI_ClearsIFF1_PreservesIFF2()
    {
        Cpu.IFF1 = true;
        Cpu.IFF2 = true;
        Load(0x0000, 0x00);
        Cpu.TriggerNmi();
        Step();
        Assert.False(Cpu.IFF1);
        Assert.True(Cpu.IFF2);
    }

    [Fact]
    public void NMI_AcceptedEvenWhenIFF1False()
    {
        Cpu.IFF1 = false;
        Load(0x0000, 0x00);
        Cpu.TriggerNmi();
        Step();
        Assert.Equal(0x0066, Cpu.PC);
    }

    [Fact]
    public void RETN_RestoresIFF1FromIFF2()
    {
        Cpu.SP = 0x01FE;
        Ram.Write(0x01FE, 0x00);
        Ram.Write(0x01FF, 0x10); // return to 0x1000
        Cpu.IFF1 = false;
        Cpu.IFF2 = true;
        Load(0x0066, 0xED, 0x45); // RETN at NMI vector
        Cpu.PC = 0x0066;
        Step();
        Assert.Equal(0x1000, Cpu.PC);
        Assert.True(Cpu.IFF1);
    }

    // ── INT IM 1 ──────────────────────────────────────────────────────────────

    [Fact]
    public void INT_IM1_JumpsTo0038_WhenIFF1True()
    {
        Cpu.IFF1 = true;
        Cpu.SP = 0x0200;
        Load(0x0100, 0x00); // NOP — instruction at current PC
        Cpu.PC = 0x0100;
        Cpu.TriggerInt();
        Step();
        Assert.Equal(0x0038, Cpu.PC);
        Assert.Equal(0x01FE, Cpu.SP);
        Assert.Equal(13UL, Cpu.TotalCycles); // 2 (acknowledge) + 11 (push+jump)
    }

    [Fact]
    public void INT_Ignored_WhenIFF1False()
    {
        Cpu.IFF1 = false;
        Load(0x0000, 0x00); // NOP
        Cpu.TriggerInt();
        Step();
        Assert.Equal(0x0001, Cpu.PC); // NOP executed normally
        Assert.NotEqual(0x0038, Cpu.PC);
    }

    [Fact]
    public void INT_ClearsIFF1AndIFF2()
    {
        Cpu.IFF1 = true;
        Cpu.IFF2 = true;
        Cpu.SP = 0x0200;
        Load(0x0000, 0x00);
        Cpu.TriggerInt();
        Step();
        Assert.False(Cpu.IFF1);
        Assert.False(Cpu.IFF2);
    }

    // ── INT IM 2 ──────────────────────────────────────────────────────────────

    [Fact]
    public void INT_IM2_JumpsViaVectorTable()
    {
        // I=0x20, data bus=0x10 → vector address = 0x2010
        // memory[0x2010] = 0xAB, memory[0x2011] = 0xCD → jump to 0xCDAB
        Cpu.IFF1 = true;
        Cpu.SP = 0x0200;
        Cpu.I = 0x20;
        Ram.Write(0x2010, 0xAB);
        Ram.Write(0x2011, 0xCD);
        Load(0x0000, 0xED, 0x5E); // IM 2
        Step();
        Load(0x0002, 0x00); // NOP at next PC
        Cpu.TriggerInt(0x10);
        Step();
        Assert.Equal(0xCDAB, Cpu.PC);
    }

    // ── HALT + interrupt ──────────────────────────────────────────────────────

    [Fact]
    public void NMI_UnhaltsAndServicesInterrupt()
    {
        Cpu.SP = 0x0200;
        Load(0x0100, 0x76); // HALT
        Cpu.PC = 0x0100;
        Step(); // enter HALT
        Assert.Equal(0x0100, Cpu.PC);

        Cpu.TriggerNmi();
        Step(); // should unhalt and service NMI
        Assert.Equal(0x0066, Cpu.PC);
    }
}
