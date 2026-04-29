using Xunit;

namespace CpuZ80.Tests;

public class UndocumentedOpcodeTests : CpuFixture
{
    // ── RETN aliases (ED 55/5D/65/6D/75/7D) ──────────────────────────────────

    [Theory]
    [InlineData(0x55)]
    [InlineData(0x5D)]
    [InlineData(0x65)]
    [InlineData(0x6D)]
    [InlineData(0x75)]
    [InlineData(0x7D)]
    public void RETN_Alias_ReturnAndRestoresIFF1(byte edOpcode)
    {
        Cpu.SP = 0x01FE;
        Ram.Write(0x01FE, 0x00);
        Ram.Write(0x01FF, 0x20); // return to 0x2000
        Cpu.IFF1 = false;
        Cpu.IFF2 = true;
        Load(0x0000, 0xED, edOpcode);
        Step();
        Assert.Equal(0x2000, Cpu.PC);
        Assert.True(Cpu.IFF1);
    }

    // ── IM aliases ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x4E)]
    [InlineData(0x66)]
    [InlineData(0x6E)]
    public void IM0_Alias_SetsMode0(byte edOpcode)
    {
        Cpu.IFF1 = true;
        Cpu.SP = 0x0200;
        Load(0x0000, 0xED, edOpcode);
        Step();
        // Trigger INT — IM 0 executes the data bus byte as an opcode (RST 38h = 0xFF)
        Cpu.TriggerInt(0xFF);
        Step();
        Assert.Equal(0x0038, Cpu.PC); // RST 38h
    }

    [Fact]
    public void IM1_Alias_ED76_SetsMode1()
    {
        Cpu.IFF1 = true;
        Cpu.SP = 0x0200;
        Load(0x0000, 0xED, 0x76); // IM 1 alias
        Step();
        Cpu.TriggerInt();
        Step();
        Assert.Equal(0x0038, Cpu.PC);
    }

    [Fact]
    public void IM2_Alias_ED7E_SetsMode2()
    {
        Cpu.IFF1 = true;
        Cpu.SP = 0x0200;
        Cpu.I = 0x30;
        Ram.Write(0x3008, 0x00);
        Ram.Write(0x3009, 0x40); // vector → 0x4000
        Load(0x0000, 0xED, 0x7E); // IM 2 alias
        Step();
        Cpu.TriggerInt(0x08);
        Step();
        Assert.Equal(0x4000, Cpu.PC);
    }

    // ── ED 63: LD (nn), HL ────────────────────────────────────────────────────

    [Fact]
    public void ED63_LD_nn_HL_StoresHL()
    {
        Cpu.HL = 0x1234;
        Load(0x0000, 0xED, 0x63, 0x00, 0x50); // LD (0x5000), HL
        Step();
        Assert.Equal(0x34, Ram.Read(0x5000));
        Assert.Equal(0x12, Ram.Read(0x5001));
        Assert.Equal(20UL, Cpu.TotalCycles);
    }

    // ── ED 6B: LD HL, (nn) ────────────────────────────────────────────────────

    [Fact]
    public void ED6B_LD_HL_nn_LoadsHL()
    {
        Ram.Write(0x5000, 0x78);
        Ram.Write(0x5001, 0x56);
        Load(0x0000, 0xED, 0x6B, 0x00, 0x50); // LD HL, (0x5000)
        Step();
        Assert.Equal(0x5678, Cpu.HL);
        Assert.Equal(20UL, Cpu.TotalCycles);
    }
}
