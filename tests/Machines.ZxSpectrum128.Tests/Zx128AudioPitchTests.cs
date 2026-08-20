using Xunit;
using Machines.Common;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// That the 128 plays the AY at the pitch it should.
/// </summary>
/// <remarks>
/// The machine used to hand <c>Render</c> raw Z80 T-states, but the AY expects
/// its own clock cycles and runs at half the CPU rate. Every channel therefore
/// ran twice as fast and all music played an octave high.
///
/// It survived because every existing audio test asked whether the output
/// changed, and it did — just at the wrong rate. These measure the rate.
/// </remarks>
public class Zx128AudioPitchTests
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

    private static byte[] Rom() => new byte[0x8000];

    /// <summary>Counts transitions between silence and sound across the capture.</summary>
    private static int CountEdges(List<short> samples)
    {
        int edges = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if ((samples[i] != 0) != (samples[i - 1] != 0)) edges++;
        }
        return edges;
    }

    [Fact]
    public void AToneIsPlayedAtThePitchTheRegistersAskFor()
    {
        // The tone counter ticks at the PSG clock over 16 and toggles every
        // `period` ticks. Period 0x100 at 1.7734 MHz therefore toggles
        // 1773400 / 16 / 256 = 433 times a second — each toggle one edge.
        //
        // Clocking from the CPU instead gives twice that, which is the octave.
        var sink = new RecordingSink();
        var machine = new Zx128Machine(Rom(), keyboard: null, audio: sink);
        machine.Reset();

        machine.WritePort(0xFFFD, 0); machine.WritePort(0xBFFD, 0x00);   // tone fine
        machine.WritePort(0xFFFD, 1); machine.WritePort(0xBFFD, 0x01);   // tone coarse: period 0x100
        machine.WritePort(0xFFFD, 7); machine.WritePort(0xBFFD, 0x3E);   // channel A tone on
        machine.WritePort(0xFFFD, 8); machine.WritePort(0xBFFD, 0x0F);   // full volume

        var video = new NullVideo();
        for (int i = 0; i < 25; i++)
        {
            machine.RunFrame();
            machine.RenderFrame(video);
        }

        // Half a second at ~433 Hz is about 217 edges; the old behaviour gave
        // about 434.
        int edges = CountEdges(sink.Samples);

        Assert.InRange(edges, 160, 300);
    }

    [Fact]
    public void HalvingTheToneperiodDoublesThePitch()
    {
        // A relative check as well as an absolute one: whatever the clock, the
        // relationship between the register and the pitch must hold.
        static int EdgesForPeriod(byte coarse)
        {
            var sink = new RecordingSink();
            var machine = new Zx128Machine(Rom(), keyboard: null, audio: sink);
            machine.Reset();

            machine.WritePort(0xFFFD, 0); machine.WritePort(0xBFFD, 0x00);
            machine.WritePort(0xFFFD, 1); machine.WritePort(0xBFFD, coarse);
            machine.WritePort(0xFFFD, 7); machine.WritePort(0xBFFD, 0x3E);
            machine.WritePort(0xFFFD, 8); machine.WritePort(0xBFFD, 0x0F);

            var video = new NullVideo();
            for (int i = 0; i < 25; i++)
            {
                machine.RunFrame();
                machine.RenderFrame(video);
            }

            return CountEdges(sink.Samples);
        }

        int low = EdgesForPeriod(0x02);    // period 0x200
        int high = EdgesForPeriod(0x01);   // period 0x100, an octave up

        Assert.InRange(high, low * 3 / 2, low * 3);
    }
}
