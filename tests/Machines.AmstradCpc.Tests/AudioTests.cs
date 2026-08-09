using Xunit;
using Machines.AmstradCpc;
using Machines.Common;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// The CPC audio path: CPU → PPI → PSG → sink.
/// </summary>
/// <remarks>
/// The PSG is the same AY-3-8912 the 128 uses, so the chip itself is already
/// covered. What is new here is that it is reached through the PPI rather than
/// a port, and that it is clocked at 1 MHz rather than the CPU clock.
/// </remarks>
public class AudioTests
{
    private sealed class RecordingSink : IAudioSink
    {
        public readonly List<short> Samples = [];
        public int SampleRate;
        public int Calls;

        public void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate)
        {
            Calls++;
            SampleRate = sampleRate;
            foreach (short s in samples) Samples.Add(s);
        }

        public int Peak => Samples.Count == 0 ? 0 : Samples.Max(s => Math.Abs((int)s));
    }

    private sealed class FakeKeyboard : IPhysicalKeyboard
    {
        public readonly HashSet<PhysicalKey> Down = [];
        public bool IsKeyDown(PhysicalKey key) => Down.Contains(key);
    }

    private static CpcMachine Build(IAudioSink? audio, IPhysicalKeyboard? keyboard = null) =>
        new(CpcBootTests.TestRom(), CpcBootTests.TestRom(), keyboard: keyboard, audio: audio);

    [Fact]
    public void SamplesAreSubmittedAtTheHostSampleRate()
    {
        var sink = new RecordingSink();
        var machine = Build(sink);
        machine.Reset();

        machine.RunFrame();
        machine.RenderFrame(new NullVideo());

        Assert.Equal(1, sink.Calls);
        Assert.Equal(44100, sink.SampleRate);

        // A 50 Hz frame at 44.1 kHz is 882 samples.
        Assert.InRange(sink.Samples.Count, 800, 960);
    }

    [Fact]
    public void SilenceByDefault()
    {
        var sink = new RecordingSink();
        var machine = Build(sink);
        machine.Reset();

        for (int i = 0; i < 5; i++) { machine.RunFrame(); machine.RenderFrame(new NullVideo()); }

        Assert.Equal(0, sink.Peak);
    }

    [Fact]
    public void WritingThePsgThroughThePpi_ProducesOutput()
    {
        // The PSG has no port of its own on a CPC: the value goes to PPI port A
        // and port C's top two bits carry BDIR and BC1.
        var sink = new RecordingSink();
        var machine = Build(sink);
        machine.Reset();

        WritePsg(machine, 0, 0x40);    // channel A tone period, fine
        WritePsg(machine, 1, 0x00);
        WritePsg(machine, 7, 0x3E);    // mixer: channel A tone on
        WritePsg(machine, 8, 0x0F);    // channel A full volume

        for (int i = 0; i < 5; i++) { machine.RunFrame(); machine.RenderFrame(new NullVideo()); }

        Assert.True(sink.Peak > 0, "a tone written through the PPI should reach the sink");
    }

    [Fact]
    public void ThePsgIsClockedAtAQuarterOfTheCpuClock()
    {
        // Passing CPU T-states straight to the PSG would run every channel four
        // times too fast. Measured as a period: a given tone register should
        // produce the frequency the 1 MHz clock implies, not the 4 MHz one.
        var sink = new RecordingSink();
        var machine = Build(sink);
        machine.Reset();

        // The tone counter ticks at the PSG clock over 16 and toggles every
        // `period` ticks, so period 0x100 toggles at 1e6 / 16 / 256 = 244 times
        // a second. Each toggle is one edge.
        WritePsg(machine, 0, 0x00);
        WritePsg(machine, 1, 0x01);
        WritePsg(machine, 7, 0x3E);
        WritePsg(machine, 8, 0x0F);

        for (int i = 0; i < 10; i++) { machine.RunFrame(); machine.RenderFrame(new NullVideo()); }

        int edges = 0;
        for (int i = 1; i < sink.Samples.Count; i++)
        {
            if ((sink.Samples[i] != 0) != (sink.Samples[i - 1] != 0)) edges++;
        }

        // Ten frames is 0.2s, so about 49 edges. Clocking the PSG from the CPU
        // clock instead would give four times that, near 195 — well outside
        // this range, which is the point of the test.
        Assert.InRange(edges, 35, 70);
    }

    [Fact]
    public void NoAudioSink_IsNotAnError()
    {
        var machine = Build(audio: null);
        machine.Reset();

        machine.RunFrame();
        machine.RenderFrame(new NullVideo());
    }

    /// <summary>Writes a PSG register the way the firmware does, through the PPI.</summary>
    private static void WritePsg(CpcMachine machine, byte register, byte value)
    {
        const ushort PortA = 0xF400;
        const ushort PortC = 0xF600;

        machine.WritePort(PortA, register);
        machine.WritePort(PortC, 0xC0);   // BDIR=1 BC1=1: select
        machine.WritePort(PortC, 0x00);

        machine.WritePort(PortA, value);
        machine.WritePort(PortC, 0x80);   // BDIR=1 BC1=0: write
        machine.WritePort(PortC, 0x00);
    }

    private sealed class NullVideo : IVideoSink
    {
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) { }
    }
}
