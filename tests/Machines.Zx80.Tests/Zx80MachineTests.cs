using Machines.Zx80;
using Xunit;

namespace Machines.Zx80.Tests;

public class Zx80MachineTests
{
    // 4KB NOP ROM — all instructions are NOP (0x00), so the CPU advances PC by 1 each Step().
    private static byte[] NopRom()
    {
        var rom = new byte[0x1000];
        // 0x00 is NOP, which is the default for a zeroed array.
        return rom;
    }

    [Fact]
    public void Reset_SetsPCToZero()
    {
        var machine = new Zx80Machine(NopRom());
        machine.Reset();
        Assert.Equal(0x0000, machine.Cpu.PC);
    }

    [Fact]
    public void Reset_DisablesInterrupts()
    {
        var machine = new Zx80Machine(NopRom());
        machine.Reset();
        Assert.False(machine.Cpu.IFF1);
        Assert.False(machine.Cpu.IFF2);
    }

    [Fact]
    public void Reset_SetsIRegisterTo0x0E()
    {
        var machine = new Zx80Machine(NopRom());
        machine.Reset();
        Assert.Equal(0x0E, machine.Cpu.I);
    }

    [Fact]
    public void Step_ExecutesOneInstruction()
    {
        var machine = new Zx80Machine(NopRom());
        machine.Reset();
        machine.Step();
        Assert.Equal(0x0001, machine.Cpu.PC);
    }

    [Fact]
    public void RunFrame_AdvancesCyclesByAtLeastOneFrame()
    {
        var machine = new Zx80Machine(NopRom());
        machine.Reset();
        machine.RunFrame();
        Assert.True(machine.Cpu.TotalCycles >= 64167UL);
    }
}
