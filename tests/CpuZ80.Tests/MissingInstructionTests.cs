using Xunit;

namespace CpuZ80.Tests;

public class MissingInstructionTests : CpuFixture
{
    // ── HALT ─────────────────────────────────────────────────────────────────

    [Fact]
    public void HALT_SuspendsExecutionAndAddsCycles()
    {
        Load(0x0100, 0x76); // HALT
        Step();
        Assert.Equal(0x0100, Cpu.PC); // PC stays on HALT
        Assert.Equal(4UL, Cpu.TotalCycles);
    }

    [Fact]
    public void HALT_RepeatedStepKeepsPC()
    {
        Load(0x0100, 0x76);
        Step(3);
        Assert.Equal(0x0100, Cpu.PC);
        Assert.Equal(12UL, Cpu.TotalCycles);
    }

    // ── DJNZ ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DJNZ_DecrementsB_AndJumpsWhenNonZero()
    {
        Cpu.B = 3;
        Load(0x1000, 0x10, 0xFE); // DJNZ -2 (loops back to itself)
        Step();
        Assert.Equal(2, Cpu.B);
        Assert.Equal(0x1000, Cpu.PC); // jumped: $1000 + 2 - 2
        Assert.Equal(13UL, Cpu.TotalCycles);
    }

    [Fact]
    public void DJNZ_DecrementsB_AndContinuesWhenZero()
    {
        Cpu.B = 1;
        Load(0x1000, 0x10, 0x05); // DJNZ +5
        Step();
        Assert.Equal(0, Cpu.B);
        Assert.Equal(0x1002, Cpu.PC); // no jump taken
        Assert.Equal(8UL, Cpu.TotalCycles);
    }

    // ── JP (HL) / JP (IX) / JP (IY) ──────────────────────────────────────────

    [Fact]
    public void JP_HL_JumpsToHL()
    {
        Cpu.HL = 0x4000;
        Load(0x0000, 0xE9); // JP (HL)
        Step();
        Assert.Equal(0x4000, Cpu.PC);
        Assert.Equal(4UL, Cpu.TotalCycles);
    }

    [Fact]
    public void JP_IX_JumpsToIX()
    {
        Cpu.IX = 0x5000;
        Load(0x0000, 0xDD, 0xE9); // JP (IX)
        Step();
        Assert.Equal(0x5000, Cpu.PC);
        Assert.Equal(8UL, Cpu.TotalCycles);
    }

    [Fact]
    public void JP_IY_JumpsToIY()
    {
        Cpu.IY = 0x6000;
        Load(0x0000, 0xFD, 0xE9); // JP (IY)
        Step();
        Assert.Equal(0x6000, Cpu.PC);
        Assert.Equal(8UL, Cpu.TotalCycles);
    }

    // ── RST ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0xC7, 0x00)]
    [InlineData(0xCF, 0x08)]
    [InlineData(0xD7, 0x10)]
    [InlineData(0xDF, 0x18)]
    [InlineData(0xE7, 0x20)]
    [InlineData(0xEF, 0x28)]
    [InlineData(0xF7, 0x30)]
    [InlineData(0xFF, 0x38)]
    public void RST_CallsFixedVector(byte opcode, ushort vector)
    {
        Cpu.SP = 0x0200;
        Load(0x0100, opcode);
        Step();
        Assert.Equal(vector, Cpu.PC);
        Assert.Equal(0x01FE, Cpu.SP);
        Assert.Equal(0x0101, Ram.Read(0x01FF) << 8 | Ram.Read(0x01FE)); // return address pushed
        Assert.Equal(11UL, Cpu.TotalCycles);
    }

    // ── IN r, (C) / OUT (C), r ────────────────────────────────────────────────

    [Fact]
    public void IN_B_C_ReadsPortIntoB()
    {
        var ports = new TestPortBus();
        ports.Set(0x1234, 0x42);
        var cpu = new CpuZ80.Core.Cpu(Ram, ports);
        cpu.BC = 0x1234;
        Ram.Load(0x0000, new byte[] { 0xED, 0x40 }); // IN B, (C)
        cpu.PC = 0x0000;
        cpu.Step();
        Assert.Equal(0x42, cpu.B);
        Assert.Equal(12UL, cpu.TotalCycles);
    }

    [Fact]
    public void OUT_C_A_WritesAToPort()
    {
        var ports = new TestPortBus();
        var cpu = new CpuZ80.Core.Cpu(Ram, ports);
        cpu.BC = 0x0080;
        cpu.A = 0x55;
        Ram.Load(0x0000, new byte[] { 0xED, 0x79 }); // OUT (C), A
        cpu.PC = 0x0000;
        cpu.Step();
        Assert.Equal(0x55, ports.Get(0x0080));
        Assert.Equal(12UL, cpu.TotalCycles);
    }

    // ── OTIR / OTDR ───────────────────────────────────────────────────────────

    [Fact]
    public void OTIR_OutputsBlockAndTerminates()
    {
        var ports = new TestPortBus();
        var cpu = new CpuZ80.Core.Cpu(Ram, ports);
        Ram.Write(0x2000, 0x11);
        Ram.Write(0x2001, 0x22);
        Ram.Write(0x2002, 0x33);
        cpu.HL = 0x2000;
        cpu.BC = 0x0303; // C=port 3, B=count 3
        Ram.Load(0x0000, new byte[] { 0xED, 0xB3 }); // OTIR
        cpu.PC = 0x0000;
        do { cpu.Step(); } while (cpu.B != 0);
        Assert.Equal(0, cpu.B);
        Assert.Equal(0x2003, cpu.HL);
        Assert.Equal(0x33, ports.Get(0x0103)); // last write: B was 1, C=3 → port 0x0103
        Assert.True(cpu.FlagZ);
    }

    [Fact]
    public void INIR_InputsBlockAndTerminates()
    {
        var ports = new TestPortBus();
        ports.Set(0x0104, 0xAA); // B=1 on last read, C=4 → port 0x0104
        var cpu = new CpuZ80.Core.Cpu(Ram, ports);
        cpu.HL = 0x3000;
        cpu.BC = 0x0304; // C=port 4, B=count 3
        Ram.Load(0x0000, new byte[] { 0xED, 0xB2 }); // INIR
        cpu.PC = 0x0000;
        do { cpu.Step(); } while (cpu.B != 0);
        Assert.Equal(0, cpu.B);
        Assert.Equal(0x3003, cpu.HL);
        Assert.Equal(0xAA, Ram.Read(0x3002)); // last byte stored (from port 0x0104)
        Assert.True(cpu.FlagZ);
    }
}

internal class TestPortBus : CpuZ80.Core.IPortBus
{
    private readonly Dictionary<ushort, byte> _ports = new();

    public void Set(ushort port, byte value) => _ports[port] = value;
    public byte Get(ushort port) => _ports.TryGetValue(port, out var v) ? v : (byte)0xFF;

    public byte In(ushort port) => Get(port);
    public void Out(ushort port, byte value) => _ports[port] = value;
}
