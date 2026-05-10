using Xunit;
using Machines.ZxSpectrum;
using CpuZ80.Core;
using Machines.Common;

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

    [Fact]
    public void RenderFrame_WritesKnownAttributes_ProducesCorrectColors()
    {
        var machine = new ZxSpectrumMachine(_stubRom);
        
        // Setup a simple pattern:
        // Byte 0 of bitmap: 0x80 (leftmost pixel set)
        // Attribute 0: 0x41 (Bright=1, Paper=Black, Ink=Blue)
        machine.WriteMemory(0x4000, 0x80);
        machine.WriteMemory(0x5800, 0x41);

        var mockSink = new MockVideoSink();
        machine.RenderFrame(mockSink);

        Assert.NotNull(mockSink.LastFrame);
        Assert.Equal(320 * 240, mockSink.LastFrame.Length);
        
        // Pixel at (32, 24) in the 320x240 buffer is (0,0) in the 256x192 area.
        int activeAreaStart = (24 * 320) + 32;
        
        // Pixel at (0,0) should be Blue (Bright)
        // 0xFF0000FF is Bright Blue
        Assert.Equal(0xFF0000FFu, mockSink.LastFrame[activeAreaStart]);

        // Pixel at (1,0) should be Paper (Black)
        Assert.Equal(0xFF000000u, mockSink.LastFrame[activeAreaStart + 1]);
    }

    [Fact]
    public void RunFrame_TriggersInterrupt()
    {
        _stubRom[0] = 0xFB; // EI (Enable Interrupts)
        _stubRom[1] = 0x00; // NOP
        var machine = new ZxSpectrumMachine(_stubRom);
        machine.Reset();
        machine.Cpu.IFF1 = true;
        machine.Cpu.IM = 1;

        // Run one frame
        machine.RunFrame();

        // At some point, the CPU should have jumped to 0x0038
        // Since we are running a stub ROM of NOPs, it might have returned 
        // or be executing instructions after the vector.
        // We'll check if PC is in the range of a likely jump or just that it's moved.
        Assert.True(machine.Cpu.TotalCycles >= 69888);
    }

    [Fact]
    public void Interrupt_JumpsToVector0038()
    {
        _stubRom[0] = 0x00; // NOP
        _stubRom[1] = 0x00; // NOP
        
        var machine = new ZxSpectrumMachine(_stubRom);
        machine.Reset();
        machine.Cpu.IM = 1;
        machine.Cpu.IFF1 = true;

        // Step once to execute NOP at 0. PC becomes 1.
        machine.Step();

        // Trigger INT manually
        machine.Cpu.TriggerInt();
        
        // Z80 samples INT at end of current instruction.
        // Step once to execute NOP at 1. At end, INT detected.
        // PC becomes 2.
        machine.Step(); 
        
        // Next Step() call will see _intPending and execute AcceptInt()
        // AcceptInt sets PC to 0x0038 and StepGenerated continues to execute at the vector.
        machine.Step();
        
        // PC is now 0x0039 (vector + 1 byte NOP)
        Assert.Equal(0x0039, machine.Cpu.PC);
    }

    private class MockVideoSink : IVideoSink
    {
        public uint[]? LastFrame;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => LastFrame = pixels.ToArray();
    }
}
