using Xunit;
using Machines.ZxSpectrum;
using CpuZ80.Core;

namespace Machines.ZxSpectrum.Tests;

public class ZxSpectrumMachineTests
{
    private readonly byte[] _stubRom = new byte[16384];

    [Fact]
    public void Reset_SetsPCAndI()
    {
        var machine = new ZxSpectrumMachine(_stubRom);
        machine.Reset();
        
        Assert.Equal(0x0000, machine.Cpu.PC);
        Assert.Equal(0x3F, machine.Cpu.I);
    }

    [Fact]
    public void MemoryMap_WiresRomAndRam()
    {
        _stubRom[0x1234] = 0x55;
        var machine = new ZxSpectrumMachine(_stubRom);
        
        // ROM
        Assert.Equal(0x55, machine.ReadMemory(0x1234));
        
        // RAM
        machine.WriteMemory(0x4000, 0xAA);
        Assert.Equal(0xAA, machine.ReadMemory(0x4000));
        
        machine.WriteMemory(0xFFFF, 0xBB);
        Assert.Equal(0xBB, machine.ReadMemory(0xFFFF));
    }
}
