using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Arithmetic;

/// <summary>
/// Flag behaviour of the 16-bit <c>ADC HL,rr</c> and <c>SBC HL,rr</c>.
/// </summary>
/// <remarks>
/// These set S, Z, H, P/V, N and C. The Zero flag matters most: <c>SBC HL,rr</c>
/// followed by <c>JR Z</c> is the standard way to compare two 16-bit values, so
/// a stale Z flag silently breaks comparisons throughout any real program.
/// </remarks>
public class Arithmetic16FlagTests
{
    private sealed class StubBus : IBus
    {
        private readonly byte[] _mem = new byte[65536];
        public byte Read(ushort a) => _mem[a];
        public void Write(ushort a, byte v) => _mem[a] = v;
        public void Poke(ushort a, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) _mem[a + i] = bytes[i];
        }
    }

    private static Cpu Run(ushort hl, ushort de, bool carryIn, params byte[] program)
    {
        var bus = new StubBus();
        bus.Poke(0x0000, program);
        var cpu = new Cpu(bus);
        cpu.Reset();
        cpu.HL = hl;
        cpu.DE = de;
        cpu.FlagC = carryIn;
        cpu.Step();
        return cpu;
    }

    // ED 52 = SBC HL,DE   |   ED 5A = ADC HL,DE
    private static readonly byte[] SbcHlDe = [0xED, 0x52];
    private static readonly byte[] AdcHlDe = [0xED, 0x5A];

    [Fact]
    public void SbcHlDe_EqualValues_SetsZero()
    {
        var cpu = Run(0x1234, 0x1234, carryIn: false, SbcHlDe);

        Assert.Equal(0x0000, cpu.HL);
        Assert.True(cpu.FlagZ, "Z must be set when the result is zero");
        Assert.False(cpu.FlagS);
        Assert.False(cpu.FlagC);
    }

    [Fact]
    public void SbcHlDe_DifferentValues_ClearsZero()
    {
        var cpu = Run(0x1234, 0x1000, carryIn: false, SbcHlDe);

        Assert.Equal(0x0234, cpu.HL);
        Assert.False(cpu.FlagZ);
    }

    [Fact]
    public void SbcHlDe_NegativeResult_SetsSign()
    {
        // 0x1000 - 0x2000 = 0xF000: bit 15 set, and a borrow.
        var cpu = Run(0x1000, 0x2000, carryIn: false, SbcHlDe);

        Assert.Equal(0xF000, cpu.HL);
        Assert.True(cpu.FlagS, "S must reflect bit 15 of the result");
        Assert.True(cpu.FlagC, "C must be set on borrow");
        Assert.False(cpu.FlagZ);
    }

    [Fact]
    public void SbcHlDe_HonoursCarryIn()
    {
        // 0x1234 - 0x1234 - 1 = 0xFFFF
        var cpu = Run(0x1234, 0x1234, carryIn: true, SbcHlDe);

        Assert.Equal(0xFFFF, cpu.HL);
        Assert.False(cpu.FlagZ);
        Assert.True(cpu.FlagS);
        Assert.True(cpu.FlagC);
    }

    [Fact]
    public void AdcHlDe_ResultZero_SetsZero()
    {
        // 0x8000 + 0x8000 = 0x10000 -> 0x0000 with carry out.
        var cpu = Run(0x8000, 0x8000, carryIn: false, AdcHlDe);

        Assert.Equal(0x0000, cpu.HL);
        Assert.True(cpu.FlagZ, "Z must be set when the result is zero");
        Assert.True(cpu.FlagC);
        Assert.False(cpu.FlagS);
    }

    [Fact]
    public void AdcHlDe_NegativeResult_SetsSign()
    {
        var cpu = Run(0x4000, 0x4000, carryIn: false, AdcHlDe);

        Assert.Equal(0x8000, cpu.HL);
        Assert.True(cpu.FlagS, "S must reflect bit 15 of the result");
        Assert.False(cpu.FlagZ);
        Assert.False(cpu.FlagC);
    }

    [Fact]
    public void AdcHlDe_PositiveResult_ClearsSignAndZero()
    {
        var cpu = Run(0x0100, 0x0200, carryIn: false, AdcHlDe);

        Assert.Equal(0x0300, cpu.HL);
        Assert.False(cpu.FlagS);
        Assert.False(cpu.FlagZ);
        Assert.False(cpu.FlagC);
    }

    [Fact]
    public void SbcHlHl_WithCarryClear_IsAlwaysZero()
    {
        // SBC HL,HL (ED 62) with C clear is the idiomatic "HL = 0, set Z".
        var bus = new StubBus();
        bus.Poke(0x0000, 0xED, 0x62);
        var cpu = new Cpu(bus);
        cpu.Reset();
        cpu.HL = 0xABCD;
        cpu.FlagC = false;
        cpu.Step();

        Assert.Equal(0x0000, cpu.HL);
        Assert.True(cpu.FlagZ);
    }

    [Fact]
    public void SbcHlSp_AsUsedByTestRoom()
    {
        // The ZX Spectrum ROM's TEST-ROOM does SBC HL,SP then RET C to decide
        // whether there is enough free memory. Both C and Z must be right.
        var bus = new StubBus();
        bus.Poke(0x0000, 0xED, 0x72);   // SBC HL,SP
        var cpu = new Cpu(bus);
        cpu.Reset();
        cpu.SP = 0xFF58;
        cpu.HL = 0x5CCE;
        cpu.FlagC = false;
        cpu.Step();

        // 0x5CCE - 0xFF58 borrows, so C is set and the ROM returns "enough room".
        Assert.True(cpu.FlagC);
        Assert.False(cpu.FlagZ);
    }
}
