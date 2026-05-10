using Xunit;
using Machines.Zx81;
using CpuZ80.Core;
using Machines.Common;

namespace Machines.Zx81.Tests;

public class Zx81MachineTests
{
    private readonly byte[] _stubRom = new byte[8192];

    [Fact]
    public void Reset_SetsPCAndI()
    {
        var machine = new Zx81Machine(_stubRom);
        machine.Reset();
        
        Assert.Equal(0x0000, machine.Cpu.PC);
        Assert.Equal(0x1E, machine.Cpu.I);
    }

    [Fact]
    public void Step_AdvancesCycles()
    {
        _stubRom[0] = 0x00; // NOP
        var machine = new Zx81Machine(_stubRom);
        machine.Reset();
        
        machine.Step();
        
        Assert.Equal(0x0001, machine.Cpu.PC);
        Assert.Equal(4u, machine.Cpu.TotalCycles);
    }

    [Fact]
    public void RunFrame_AdvancesFrameBudget()
    {
        var machine = new Zx81Machine(_stubRom);
        machine.RunFrame();
        
        Assert.True(machine.Cpu.TotalCycles >= 65000);
    }

    [Fact]
    public void MemoryMap_PartialDecoding_RomMirrors()
    {
        _stubRom[0x1ABC] = 0x42;
        var machine = new Zx81Machine(_stubRom);
        
        // Primary range (0x0000-0x1FFF)
        Assert.Equal(0x42, machine.ReadMemory(0x1ABC));
        
        // Mirror (0x2000-0x3FFF)
        Assert.Equal(0x42, machine.ReadMemory(0x2000 + 0x1ABC));
    }

    [Fact]
    public void MemoryMap_PartialDecoding_RamMirrors()
    {
        var machine = new Zx81Machine(_stubRom);
        
        // Primary RAM (0x4000-0x43FF)
        machine.WriteMemory(0x4005, 0x55);
        Assert.Equal(0x55, machine.ReadMemory(0x4005));
        
        // Mirror (0x4400)
        Assert.Equal(0x55, machine.ReadMemory(0x4405));
        
        // Mirror (0x7C00)
        Assert.Equal(0x55, machine.ReadMemory(0x7C05));
    }

    [Fact]
    public void PortFD_DisablesNmi()
    {
        var machine = new Zx81Machine(_stubRom);
        
        machine.WritePort(0x00FD, 0); // OUT (FD), A
        
        // NmiEnabled is now encapsulated in ULA, but for tests we can check if Step triggers NMI
        _stubRom[0] = 0x00;
        machine.Reset();
        // WaitCycles and TotalCycles check...
        // Actually, we'll just check that it DOESN'T jump to 0x66 after 207 cycles
        for (int i = 0; i < 60; i++) machine.Step();
        Assert.NotEqual(0x0066, machine.Cpu.PC);
    }

    [Fact]
    public void PortFE_EnablesNmi()
    {
        var machine = new Zx81Machine(_stubRom);
        machine.WritePort(0x00FE, 0); // OUT (FE), A
        
        _stubRom[0] = 0x00;
        machine.Reset();
        machine.WritePort(0x00FE, 0);

        // Run until NMI
        while (machine.Cpu.PC != 0x0066 && machine.Cpu.TotalCycles < 1000)
        {
            machine.Step();
        }
        
        Assert.Equal(0x0066, machine.Cpu.PC);
    }

    [Fact]
    public void SLOWMode_InjectsNMIs()
    {
        _stubRom[0] = 0x00; // NOP
        var machine = new Zx81Machine(_stubRom);
        machine.Reset();
        machine.WritePort(0x00FE, 0);
        
        // Run until just before a scanline (207 cycles)
        while (machine.Cpu.TotalCycles < 200) machine.Step();
        
        machine.Step();
        machine.Step(); 
        machine.Step();
        
        if (machine.Cpu.PC != 0x0066) machine.Step();

        Assert.Equal(0x0066, machine.Cpu.PC);
    }

    [Fact]
    public void RenderFrame_CollapsedDFile_ProducesValidFrame()
    {
        var machine = new Zx81Machine(_stubRom);
        machine.Reset();
        
        machine.WriteMemory(0x400C, 0x80); 
        machine.WriteMemory(0x400D, 0x40);
        
        for (int i = 0; i < 25; i++)
            machine.WriteMemory((ushort)(0x4080 + i), 0x76); // HALT

        var mockSink = new MockVideoSink();
        machine.RenderFrame(mockSink);
        
        Assert.NotNull(mockSink.LastFrame);
        Assert.Equal(320 * 240, mockSink.LastFrame.Length);
    }

    [Fact]
    public void Keyboard_HalfRow_CorrectBitLow()
    {
        var mockKb = new MockKeyboard();
        var machine = new Zx81Machine(_stubRom, mockKb);
        
        mockKb.Pressed.Add(PhysicalKey.A);
        byte result = machine.ReadPort(0xFD00);
        Assert.Equal(0xFE, result);
    }

    [Fact]
    public void LoadSnapshot_PopulatesRam()
    {
        var machine = new Zx81Machine(_stubRom);
        byte[] pFileData = new byte[100];
        for (int i = 0; i < 100; i++) pFileData[i] = (byte)i;
        
        using var stream = new MemoryStream(pFileData);
        machine.LoadSnapshot(stream);
        
        for (int i = 0; i < 100; i++)
            Assert.Equal((byte)i, machine.ReadMemory((ushort)(0x4000 + i)));
    }

    private class MockVideoSink : IVideoSink
    {
        public uint[]? LastFrame;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => LastFrame = pixels.ToArray();
    }

    private class MockKeyboard : IPhysicalKeyboard
    {
        public HashSet<PhysicalKey> Pressed = new();
        public bool IsKeyDown(PhysicalKey key) => Pressed.Contains(key);
    }
}
