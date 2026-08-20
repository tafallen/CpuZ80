using Xunit;
using Machines.Common;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// That the +3 plays the AY at the pitch it should.
/// </summary>
/// <remarks>
/// The same defect as the 128's, and it needed the same one-line fix, because
/// the two machines have separate copies of the audio path. That duplication is
/// why this test exists separately rather than being assumed covered: the copies
/// can drift, and a shared bug fixed in one place stays in the other.
/// </remarks>
public class Plus3AudioPitchTests
{
    private sealed class RecordingSink : IAudioSink
    {
        public readonly List<short> Samples = [];

        public void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate)
        {
            foreach (short s in samples) Samples.Add(s);
        }
    }

    private sealed class NullVideo : IVideoSink
    {
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) { }
    }

    [Fact]
    public void AToneIsPlayedAtThePitchTheRegistersAskFor()
    {
        // Period 0x100 at the AY's 1.7734 MHz toggles about 433 times a second.
        // Clocking it from the CPU instead gives twice that: an octave high.
        var sink = new RecordingSink();
        var machine = new Plus3Machine(new byte[0x10000], keyboard: null, audio: sink);
        machine.Reset();

        machine.WritePort(0xFFFD, 0); machine.WritePort(0xBFFD, 0x00);
        machine.WritePort(0xFFFD, 1); machine.WritePort(0xBFFD, 0x01);
        machine.WritePort(0xFFFD, 7); machine.WritePort(0xBFFD, 0x3E);
        machine.WritePort(0xFFFD, 8); machine.WritePort(0xBFFD, 0x0F);

        var video = new NullVideo();
        for (int i = 0; i < 25; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(video);
        }

        int edges = 0;
        for (int i = 1; i < sink.Samples.Count; i++)
        {
            if ((sink.Samples[i] != 0) != (sink.Samples[i - 1] != 0)) edges++;
        }

        Assert.InRange(edges, 160, 300);
    }
}
