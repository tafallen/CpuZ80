using Xunit;
using Machines.Common;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// Border and beeper output must describe the frame that was just executed.
/// </summary>
/// <remarks>
/// Both are captured as timestamped transition lists while the CPU runs, then
/// replayed against the frame's T-state window at render time. If the list and
/// the window come from different frames, every transition falls before the
/// window and gets collapsed into a single flat value — a border with no stripes
/// and a beeper with no tone.
/// </remarks>
public class FrameSequencingTests
{
    private const int TotalWidth = 320;
    private const int BorderHeight = 24;

    private sealed class CaptureVideo : IVideoSink
    {
        public uint[] Frame = [];
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) => Frame = pixels.ToArray();
    }

    private sealed class CaptureAudio : IAudioSink
    {
        public short[] Samples = [];
        public void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate) => Samples = samples.ToArray();
    }

    private static ZxSpectrumMachine MachineRunning(byte[] program, IAudioSink? audio = null)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000], audio: audio);
        machine.Reset();
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(0x8000 - 0x4000 + i), program[i]);
        machine.Cpu.PC = 0x8000;
        return machine;
    }

    /// <summary>Flips the border between blue (1) and red (2) as fast as the CPU can.</summary>
    private static byte[] StripedBorderProgram =>
    [
        0x3E, 0x01,       // LD A,1
        0xD3, 0xFE,       // OUT (0xFE),A
        0x3E, 0x02,       // LD A,2
        0xD3, 0xFE,       // OUT (0xFE),A
        0xC3, 0x00, 0x80, // JP 0x8000
    ];

    /// <summary>Toggles the speaker bit continuously, producing a square wave.</summary>
    private static byte[] SpeakerToneProgram =>
    [
        0x3E, 0x10,       // LD A,0x10  (speaker on)
        0xD3, 0xFE,       // OUT (0xFE),A
        0x06, 0x20,       // LD B,0x20
        0x10, 0xFE,       // DJNZ $      (delay)
        0x3E, 0x00,       // LD A,0x00  (speaker off)
        0xD3, 0xFE,       // OUT (0xFE),A
        0x06, 0x20,       // LD B,0x20
        0x10, 0xFE,       // DJNZ $      (delay)
        0xC3, 0x00, 0x80, // JP 0x8000
    ];

    [Fact]
    public void StripedBorder_ProducesMoreThanOneColourInTheSameFrame()
    {
        var machine = MachineRunning(StripedBorderProgram);
        var sink = new CaptureVideo();

        // Run a few frames so any one-frame skew has settled; the effect is
        // continuous, so every rendered frame should be striped.
        for (int i = 0; i < 4; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(sink);
        }

        var distinct = new HashSet<uint>();
        for (int i = 0; i < BorderHeight * TotalWidth; i++) distinct.Add(sink.Frame[i]);

        Assert.True(distinct.Count > 1,
            $"Top border should show the border changing mid-frame, but was a flat " +
            $"{string.Join(", ", distinct.Select(c => $"0x{c:X8}"))}");
    }

    [Fact]
    public void BorderTransitions_AreNotCarriedIntoTheFollowingFrame()
    {
        // Set the border once, mid-stream, and confirm the change shows up in the
        // frame it happened in rather than leaking into later ones.
        var machine = MachineRunning([0x00, 0xC3, 0x00, 0x80]); // NOP; JP $
        var sink = new CaptureVideo();

        machine.WritePort(0x00FE, 0x01); // blue
        machine.RunFrame();
        machine.RenderFrame(sink);
        Assert.Equal(0xFFD70000u, sink.Frame[0]); // blue, RGBA

        machine.WritePort(0x00FE, 0x02); // red
        machine.RunFrame();
        machine.RenderFrame(sink);
        Assert.Equal(0xFF0000D7u, sink.Frame[0]); // red, RGBA
    }

    [Fact]
    public void Beeper_ProducesAVaryingWaveformWithinOneFrame()
    {
        var audio = new CaptureAudio();
        var machine = MachineRunning(SpeakerToneProgram, audio);

        for (int i = 0; i < 4; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(new CaptureVideo());
        }

        Assert.NotEmpty(audio.Samples);

        // Count edges rather than distinct values. Collapsing every transition
        // into the first sample yields one glitch and then a flat DC level —
        // which has two distinct values and would pass a naive check while
        // sounding like a 50 Hz tick instead of a tone.
        int edges = 0;
        for (int i = 1; i < audio.Samples.Length; i++)
            if (audio.Samples[i] != audio.Samples[i - 1]) edges++;

        Assert.True(edges > 10,
            $"A square wave should switch level many times across the frame, but only " +
            $"{edges} edge(s) appeared in {audio.Samples.Length} samples " +
            $"(first few: {string.Join(",", audio.Samples.Take(8))})");
    }

    [Fact]
    public void Beeper_SilenceProducesAFlatWaveform()
    {
        // Control for the test above: with no speaker activity the output must be
        // flat, so a varying waveform there really does come from the OUTs.
        var audio = new CaptureAudio();
        var machine = MachineRunning([0x00, 0xC3, 0x00, 0x80], audio); // NOP; JP $

        for (int i = 0; i < 4; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(new CaptureVideo());
        }

        Assert.NotEmpty(audio.Samples);
        Assert.Single(new HashSet<short>(audio.Samples));
    }
}
