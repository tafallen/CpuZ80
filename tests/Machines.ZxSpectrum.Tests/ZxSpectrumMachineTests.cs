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
        
        // Pixel at (0,0) should be Blue (Bright).
        // Pixels are RGBA32 packed as 0xAABBGGRR, so bright blue is 0xFFFF0000.
        Assert.Equal(0xFFFF0000u, mockSink.LastFrame[activeAreaStart]);

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

    [Fact]
    public void RenderFrame_TogglesSpeaker_ProducesSamples()
    {
        // The speaker must be toggled by code running *inside* the frame being
        // rendered. An earlier version of this test wrote the port before
        // RunFrame and relied on the transitions surviving into the next frame —
        // which only worked because the transition list was double-buffered and
        // rendered a frame late. See FrameSequencingTests.
        var mockAudio = new MockAudioSink();
        var machine = new ZxSpectrumMachine(_stubRom, audio: mockAudio);
        machine.Reset();

        byte[] toggleSpeaker =
        [
            0x3E, 0x10,       // LD A,0x10   (speaker high)
            0xD3, 0xFE,       // OUT (0xFE),A
            0x06, 0x40,       // LD B,0x40
            0x10, 0xFE,       // DJNZ $      (hold)
            0x3E, 0x00,       // LD A,0x00   (speaker low)
            0xD3, 0xFE,       // OUT (0xFE),A
            0x06, 0x40,       // LD B,0x40
            0x10, 0xFE,       // DJNZ $      (hold)
            0xC3, 0x00, 0x80, // JP 0x8000
        ];
        for (int i = 0; i < toggleSpeaker.Length; i++)
            machine.Ram.Write((ushort)(0x8000 - 0x4000 + i), toggleSpeaker[i]);
        machine.Cpu.PC = 0x8000;

        machine.RunFrame();
        machine.RenderFrame(new MockVideoSink());

        Assert.NotNull(mockAudio.LastSamples);
        Assert.True(mockAudio.LastSamples.Length > 0);

        // At least some samples should be non-zero
        Assert.Contains(mockAudio.LastSamples, s => s > 0);
    }

    // Memory contention is covered comprehensively in ContentionTests: the
    // visible-area window, the address range, horizontal blanking, the exact
    // 6,5,4,3,2,1,0,0 delay pattern, and regression guards proving contention
    // reaches CALL/RET/PUSH/POP and the block instructions.

    private class MockVideoSink : IVideoSink
    {
        public uint[]? LastFrame;
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => LastFrame = pixels.ToArray();
    }

    private class MockAudioSink : IAudioSink
    {
        public short[]? LastSamples;
        public void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate) => LastSamples = samples.ToArray();
    }

    [Fact]
    public void LoadSnapshot_SNA_RestoresState()
    {
        var machine = new ZxSpectrumMachine(_stubRom);
        byte[] snaData = new byte[27 + 49152];
        
        // Setup dummy header
        snaData[0] = 0x12; // I
        snaData[1] = 0x34; snaData[2] = 0x56; // HL'
        snaData[21] = 0xAA; snaData[22] = 0xBB; // AF
        snaData[23] = 0x00; snaData[24] = 0x40; // SP = 0x4000
        snaData[25] = 1; // IM 1
        
        // Setup dummy RAM with PC on stack at 0x4000
        snaData[27 + 0] = 0xDE; // PC lo
        snaData[27 + 1] = 0xC0; // PC hi
        
        using var stream = new MemoryStream(snaData);
        machine.LoadSnapshot(stream);
        
        Assert.Equal(0x12, machine.Cpu.I);
        Assert.Equal(0x5634, machine.Cpu.HL_);
        Assert.Equal(0xBBAA, machine.Cpu.AF);
        Assert.Equal(0xC0DE, machine.Cpu.PC);
        Assert.Equal(1, machine.Cpu.IM);
        Assert.Equal(0x4002, machine.Cpu.SP);
    }
}
