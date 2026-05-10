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
    public void RenderFrame_BorderColor_ProducesCorrectPadding()
    {
        var machine = new ZxSpectrumMachine(_stubRom);
        
        // Set border to Red (2)
        machine.WritePort(0xFE, 0x02);

        var mockSink = new MockVideoSink();
        machine.RenderFrame(mockSink);

        Assert.NotNull(mockSink.LastFrame);
        
        // Pixel at (0,0) should be the border color (Red Normal)
        // 0xFFD70000 is Red Normal
        Assert.Equal(0xFFD70000u, mockSink.LastFrame[0]);
    }

    private class MockVideoSink : IVideoSink
    {
        public uint[]? LastFrame;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => LastFrame = pixels.ToArray();
    }
}
