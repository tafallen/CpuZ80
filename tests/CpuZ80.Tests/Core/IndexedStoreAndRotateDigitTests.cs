using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Core;

/// <summary>
/// The two instruction groups ZEXALL still flagged after the 128 boot fixes:
/// <c>LD (nn),IX/IY</c> and <c>RRD</c>/<c>RLD</c>.
/// </summary>
public class IndexedStoreAndRotateDigitTests
{
    private sealed class StubBus : IBus
    {
        private readonly byte[] _mem = new byte[65536];
        public byte Read(ushort a) => _mem[a];
        public void Write(ushort a, byte v) => _mem[a] = v;
        public void Poke(ushort a, params byte[] b)
        {
            for (int i = 0; i < b.Length; i++) _mem[a + i] = b[i];
        }
    }

    private static (Cpu Cpu, StubBus Bus) Machine(params byte[] program)
    {
        var bus = new StubBus();
        bus.Poke(0x0000, program);
        var cpu = new Cpu(bus);
        cpu.Reset();
        return (cpu, bus);
    }

    // ── LD (nn),IX / LD (nn),IY ──────────────────────────────────────────────

    [Fact]
    public void LdNnIx_StoresTheIndexRegister_NotHl()
    {
        // DD 22 00 90 = LD (0x9000),IX
        var (cpu, bus) = Machine(0xDD, 0x22, 0x00, 0x90);
        cpu.IX = 0x1234;
        cpu.HL = 0xBEEF;   // must not be what gets written
        cpu.Step();

        Assert.Equal(0x34, bus.Read(0x9000));
        Assert.Equal(0x12, bus.Read(0x9001));
    }

    [Fact]
    public void LdNnIy_StoresTheIndexRegister_NotHl()
    {
        // FD 22 00 90 = LD (0x9000),IY
        var (cpu, bus) = Machine(0xFD, 0x22, 0x00, 0x90);
        cpu.IY = 0xCAFE;
        cpu.HL = 0xBEEF;
        cpu.Step();

        Assert.Equal(0xFE, bus.Read(0x9000));
        Assert.Equal(0xCA, bus.Read(0x9001));
    }

    [Fact]
    public void LdIxNn_LoadsTheIndexRegister_NotHl()
    {
        // DD 2A 00 90 = LD IX,(0x9000)
        var (cpu, bus) = Machine(0xDD, 0x2A, 0x00, 0x90);
        bus.Poke(0x9000, 0x78, 0x56);
        cpu.HL = 0xBEEF;
        cpu.Step();

        Assert.Equal(0x5678, cpu.IX);
        Assert.Equal(0xBEEF, cpu.HL);   // HL untouched
    }

    [Fact]
    public void LdIyNn_LoadsTheIndexRegister_NotHl()
    {
        var (cpu, bus) = Machine(0xFD, 0x2A, 0x00, 0x90);
        bus.Poke(0x9000, 0xBC, 0x9A);
        cpu.HL = 0xBEEF;
        cpu.Step();

        Assert.Equal(0x9ABC, cpu.IY);
        Assert.Equal(0xBEEF, cpu.HL);
    }

    [Fact]
    public void LdNnHl_StillStoresHl()
    {
        // The unprefixed form must be unaffected by the fix.
        var (cpu, bus) = Machine(0x22, 0x00, 0x90);
        cpu.HL = 0x4321;
        cpu.Step();

        Assert.Equal(0x21, bus.Read(0x9000));
        Assert.Equal(0x43, bus.Read(0x9001));
    }

    // ── RRD / RLD ────────────────────────────────────────────────────────────

    [Fact]
    public void Rrd_RotatesNibblesCorrectly()
    {
        // ED 67 = RRD. A=0x84, (HL)=0x20 -> A=0x80, (HL)=0x42
        var (cpu, bus) = Machine(0xED, 0x67);
        cpu.HL = 0x9000;
        cpu.A = 0x84;
        bus.Poke(0x9000, 0x20);
        cpu.Step();

        Assert.Equal(0x80, cpu.A);
        Assert.Equal(0x42, bus.Read(0x9000));
    }

    [Fact]
    public void Rld_RotatesNibblesCorrectly()
    {
        // ED 6F = RLD. A=0x84, (HL)=0x20 -> A=0x82, (HL)=0x04
        var (cpu, bus) = Machine(0xED, 0x6F);
        cpu.HL = 0x9000;
        cpu.A = 0x84;
        bus.Poke(0x9000, 0x20);
        cpu.Step();

        Assert.Equal(0x82, cpu.A);
        Assert.Equal(0x04, bus.Read(0x9000));
    }

    [Fact]
    public void Rrd_ClearsHalfCarryAndAddSubtract()
    {
        var (cpu, bus) = Machine(0xED, 0x67);
        cpu.HL = 0x9000;
        cpu.A = 0x12;
        bus.Poke(0x9000, 0x34);
        cpu.FlagH = true;   // must be cleared
        cpu.FlagN = true;   // must be cleared
        cpu.Step();

        Assert.False(cpu.FlagH, "RRD must clear H");
        Assert.False(cpu.FlagN, "RRD must clear N");
    }

    [Fact]
    public void Rld_ClearsHalfCarryAndAddSubtract()
    {
        var (cpu, bus) = Machine(0xED, 0x6F);
        cpu.HL = 0x9000;
        cpu.A = 0x12;
        bus.Poke(0x9000, 0x34);
        cpu.FlagH = true;
        cpu.FlagN = true;
        cpu.Step();

        Assert.False(cpu.FlagH, "RLD must clear H");
        Assert.False(cpu.FlagN, "RLD must clear N");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rrd_PreservesCarry(bool carryIn)
    {
        var (cpu, bus) = Machine(0xED, 0x67);
        cpu.HL = 0x9000;
        cpu.A = 0x12;
        bus.Poke(0x9000, 0x34);
        cpu.FlagC = carryIn;
        cpu.Step();

        Assert.Equal(carryIn, cpu.FlagC);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rld_PreservesCarry(bool carryIn)
    {
        var (cpu, bus) = Machine(0xED, 0x6F);
        cpu.HL = 0x9000;
        cpu.A = 0x12;
        bus.Poke(0x9000, 0x34);
        cpu.FlagC = carryIn;
        cpu.Step();

        Assert.Equal(carryIn, cpu.FlagC);
    }

    [Fact]
    public void Rrd_SetsSignZeroAndParityFromA()
    {
        // A=0x00, (HL)=0x00 -> A stays 0x00: Z set, S clear, parity even.
        var (cpu, bus) = Machine(0xED, 0x67);
        cpu.HL = 0x9000;
        cpu.A = 0x00;
        bus.Poke(0x9000, 0x00);
        cpu.Step();

        Assert.Equal(0x00, cpu.A);
        Assert.True(cpu.FlagZ);
        Assert.False(cpu.FlagS);
        Assert.True(cpu.FlagPV);
    }

    [Fact]
    public void Rrd_SetsSignWhenResultIsNegative()
    {
        var (cpu, bus) = Machine(0xED, 0x67);
        cpu.HL = 0x9000;
        cpu.A = 0x80;
        bus.Poke(0x9000, 0x00);
        cpu.Step();

        Assert.Equal(0x80, cpu.A);
        Assert.True(cpu.FlagS);
        Assert.False(cpu.FlagZ);
    }
}
