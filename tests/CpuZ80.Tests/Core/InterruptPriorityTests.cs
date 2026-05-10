using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Core;

public class InterruptPriorityTests
{
    private class StubBus : IBus
    {
        private readonly byte[] _mem = new byte[65536];
        public byte Read(ushort address) => _mem[address];
        public void Write(ushort address, byte value) => _mem[address] = value;
    }

    [Fact]
    public void Nmi_HasPriorityOverInt()
    {
        var cpu = new Cpu(new StubBus());
        cpu.Reset();
        cpu.IFF1 = true;
        cpu.IM = 1;

        // Simultaneous NMI and INT
        cpu.TriggerNmi();
        cpu.IntPin = true;

        // Z80 checks interrupts at the start of Step()
        cpu.Step(); 
        
        // Should have handled NMI (jump to 0x66)
        Assert.Equal(0x0066, cpu.PC);
        Assert.False(cpu.IFF1);
        Assert.True(cpu.IFF2); // IFF1 saved to IFF2
    }

    [Fact]
    public void Halt_ExitsOnNmi()
    {
        var cpu = new Cpu(new StubBus());
        cpu.Reset();
        cpu.IFF1 = false; // Disable maskable interrupts

        // HALT opcode
        cpu.WriteMemory(0x0000, 0x76); 
        cpu.Step();
        Assert.True(cpu.IsHalted);

        cpu.TriggerNmi();
        cpu.Step(); // Should exit HALT and jump to 0x66
        
        Assert.False(cpu.IsHalted);
        Assert.Equal(0x0066, cpu.PC);
    }

    [Fact]
    public void Halt_ExitsOnInt_IfEnabled()
    {
        var cpu = new Cpu(new StubBus());
        cpu.Reset();
        cpu.IM = 1;
        cpu.IFF1 = true;

        cpu.WriteMemory(0x0000, 0x76);
        cpu.Step();
        Assert.True(cpu.IsHalted);

        cpu.IntPin = true;
        cpu.Step();
        
        Assert.False(cpu.IsHalted);
        Assert.Equal(0x0038, cpu.PC);
    }

    [Fact]
    public void Halt_DoesNotExitOnInt_IfDisabled()
    {
        var cpu = new Cpu(new StubBus());
        cpu.Reset();
        cpu.IFF1 = false;

        cpu.WriteMemory(0x0000, 0x76);
        cpu.Step();
        Assert.True(cpu.IsHalted);

        cpu.IntPin = true;
        cpu.Step();
        
        Assert.True(cpu.IsHalted);
        Assert.Equal(0x0000, cpu.PC);
    }
}
