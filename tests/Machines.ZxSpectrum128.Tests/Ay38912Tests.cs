using Xunit;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// General Instrument AY-3-8912 — three square-wave channels, a noise generator
/// and an envelope generator, behind 16 registers.
/// </summary>
/// <remarks>
/// On the 128: write 0xFFFD to select a register, read 0xFFFD to read it, write
/// 0xBFFD to set it. Both ports have A15 high; A14 distinguishes them — select
/// is A15=1 A14=1 (0xFFFD), data is A15=1 A14=0 (0xBFFD).
///
/// Register read-back is NOT a plain mirror — several registers have unused high
/// bits that always read back as 0.
/// </remarks>
public class Ay38912Tests
{
    private const ushort SelectPort = 0xFFFD;
    private const ushort DataPort = 0xBFFD;

    private static Ay38912 Chip() => new();

    private static void Write(Ay38912 ay, int register, byte value)
    {
        ay.Out(SelectPort, (byte)register);
        ay.Out(DataPort, value);
    }

    private static byte Read(Ay38912 ay, int register)
    {
        ay.Out(SelectPort, (byte)register);
        return ay.In(SelectPort);
    }

    // ── Register file ────────────────────────────────────────────────────────

    [Fact]
    public void SelectedRegisterIsLatched()
    {
        var ay = Chip();
        ay.Out(SelectPort, 7);
        Assert.Equal(7, ay.SelectedRegister);
    }

    [Fact]
    public void OnlyTheLowFourBitsSelectARegister()
    {
        // The chip has 16 registers; higher bits are ignored.
        var ay = Chip();
        ay.Out(SelectPort, 0x1F);
        Assert.Equal(0x0F, ay.SelectedRegister);
    }

    [Fact]
    public void WriteThenReadRoundTripsAFullWidthRegister()
    {
        var ay = Chip();
        Write(ay, 0, 0xA5);   // channel A fine tone — all 8 bits used
        Assert.Equal(0xA5, Read(ay, 0));
    }

    [Theory]
    // register, written, expected read-back — unused high bits read as 0
    [InlineData(1, 0xFF, 0x0F)]  // channel A coarse tone: 4 bits
    [InlineData(3, 0xFF, 0x0F)]  // channel B coarse tone: 4 bits
    [InlineData(5, 0xFF, 0x0F)]  // channel C coarse tone: 4 bits
    [InlineData(6, 0xFF, 0x1F)]  // noise period: 5 bits
    [InlineData(8, 0xFF, 0x1F)]  // channel A volume: 5 bits
    [InlineData(9, 0xFF, 0x1F)]  // channel B volume: 5 bits
    [InlineData(10, 0xFF, 0x1F)] // channel C volume: 5 bits
    [InlineData(13, 0xFF, 0x0F)] // envelope shape: 4 bits
    public void UnusedRegisterBitsReadBackAsZero(int register, byte written, byte expected)
    {
        var ay = Chip();
        Write(ay, register, written);
        Assert.Equal(expected, Read(ay, register));
    }

    [Theory]
    [InlineData(0)]  // channel A fine tone
    [InlineData(2)]  // channel B fine tone
    [InlineData(4)]  // channel C fine tone
    [InlineData(7)]  // mixer
    [InlineData(11)] // envelope fine
    [InlineData(12)] // envelope coarse
    public void FullWidthRegistersKeepAllEightBits(int register)
    {
        var ay = Chip();
        Write(ay, register, 0xFF);
        Assert.Equal(0xFF, Read(ay, register));
    }

    [Fact]
    public void RegistersAreIndependent()
    {
        // Registers 0-13 only: 14 and 15 are I/O ports and read the external
        // pins rather than the latch unless configured as outputs.
        var ay = Chip();
        for (int r = 0; r < 14; r++) Write(ay, r, (byte)(r * 0x11));

        for (int r = 0; r < 14; r++)
        {
            byte expected = (byte)((r * 0x11) & Ay38912.RegisterMask(r));
            Assert.Equal(expected, Read(ay, r));
        }
    }

    // ── Port decoding ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0xFFFD)] // canonical select port
    [InlineData(0xFFF9)] // A1 low, A15=1, A14=1
    [InlineData(0xC000)] // A15=1, A14=1, A1=0
    public void SelectPortRespondsWhenA15AndA14AreHighAndA1Low(ushort port)
    {
        var ay = Chip();
        ay.Out(port, 0x05);
        Assert.Equal(5, ay.SelectedRegister);
    }

    [Fact]
    public void DataPortRespondsWhenA15HighAndA14Low()
    {
        var ay = Chip();
        ay.Out(SelectPort, 0);
        ay.Out(0xBFFD, 0x77);
        Assert.Equal(0x77, Read(ay, 0));
    }

    [Theory]
    [InlineData(0x7FFD)] // paging port — A14 high but A15 low, must not select
    [InlineData(0x00FE)] // ULA port
    [InlineData(0x001F)] // Kempston
    [InlineData(0xFFFE)] // A15/A14 high but A1 high — the "all keyboard rows" port
    [InlineData(0xBFFE)] // A15 high, A14 low, but A1 high
    public void IgnoresPortsThatAreNotItsOwn(ushort port)
    {
        var ay = Chip();
        ay.Out(SelectPort, 3);
        ay.Out(port, 0x0A);

        // The selected register must not have changed.
        Assert.Equal(3, ay.SelectedRegister);
    }

    [Fact]
    public void ReadingANonAyPortReturnsOpenBus()
    {
        var ay = Chip();
        Assert.Equal(0xFF, ay.In(0x00FE));
    }

    [Fact]
    public void DoesNotAnswerTheAllKeyboardRowsPort()
    {
        // Regression guard. 0xFFFE has A15 and A14 high but A1 high too, so the
        // AY must stay off the bus. Answering it ANDs a register value into the
        // keyboard read and crashes the 128 ROM during startup.
        var ay = Chip();
        Write(ay, 0, 0x00);
        Assert.Equal(0xFF, ay.In(0xFFFE));
    }

    // ── I/O ports (registers 14 and 15) ──────────────────────────────────────

    [Fact]
    public void PortA_WhenAnInput_ReadsTheExternalPins_NotTheLatch()
    {
        // Register 7 bit 6 sets port A's direction. When it is 0 the port is an
        // input, so reading register 14 returns the pins rather than whatever was
        // last written. On a 128 the RS232/keypad socket is normally empty, so
        // they float high.
        var ay = Chip();
        Write(ay, 7, 0x00);   // bit 6 clear -> port A is an input
        Write(ay, 14, 0x00);  // latch a zero

        Assert.Equal(0xFF, Read(ay, 14));
    }

    [Fact]
    public void PortA_WhenAnOutput_ReadsBackTheLatch()
    {
        var ay = Chip();
        Write(ay, 7, 0x40);   // bit 6 set -> port A is an output
        Write(ay, 14, 0x5A);

        Assert.Equal(0x5A, Read(ay, 14));
    }

    [Fact]
    public void PortB_WhenAnInput_ReadsTheExternalPins()
    {
        // The 8912 does not bond port B at all, so it always reads as an input.
        var ay = Chip();
        Write(ay, 7, 0x00);
        Write(ay, 15, 0x00);

        Assert.Equal(0xFF, Read(ay, 15));
    }

    [Fact]
    public void PortB_WhenAnOutput_ReadsBackTheLatch()
    {
        var ay = Chip();
        Write(ay, 7, 0x80);   // bit 7 set -> port B is an output
        Write(ay, 15, 0x3C);

        Assert.Equal(0x3C, Read(ay, 15));
    }

    // ── Sound generation ─────────────────────────────────────────────────────

    [Fact]
    public void SilentByDefault()
    {
        var ay = Chip();
        short[] buffer = new short[512];
        ay.Render(buffer, 1000);

        Assert.All(buffer, s => Assert.Equal(0, s));
    }

    [Fact]
    public void AToneChannelProducesAVaryingWaveform()
    {
        var ay = Chip();
        Write(ay, 0, 0x40);   // channel A period
        Write(ay, 1, 0x00);
        Write(ay, 7, 0x3E);   // mixer: channel A tone on, everything else off
        Write(ay, 8, 0x0F);   // channel A full volume

        short[] buffer = new short[2000];
        ay.Render(buffer, 100000);

        int edges = 0;
        for (int i = 1; i < buffer.Length; i++)
            if (buffer[i] != buffer[i - 1]) edges++;

        Assert.True(edges > 10, $"a tone should switch level repeatedly, saw {edges} edges");
    }

    [Fact]
    public void VolumeZeroIsSilentEvenWithToneEnabled()
    {
        var ay = Chip();
        Write(ay, 0, 0x40);
        Write(ay, 7, 0x3E); // channel A tone on
        Write(ay, 8, 0x00); // volume 0

        short[] buffer = new short[1000];
        ay.Render(buffer, 50000);

        Assert.All(buffer, s => Assert.Equal(0, s));
    }

    [Fact]
    public void MixerCanDisableAToneChannel()
    {
        var ay = Chip();
        Write(ay, 0, 0x40);
        Write(ay, 8, 0x0F);
        Write(ay, 7, 0x3F); // all tone and noise disabled

        short[] buffer = new short[1000];
        ay.Render(buffer, 50000);

        Assert.All(buffer, s => Assert.Equal(0, s));
    }

    [Fact]
    public void LouderVolumeGivesLargerAmplitude()
    {
        static int PeakAt(byte volume)
        {
            var ay = Chip();
            Write(ay, 0, 0x40);
            Write(ay, 7, 0x3E);
            Write(ay, 8, volume);

            short[] buffer = new short[2000];
            ay.Render(buffer, 100000);

            int peak = 0;
            foreach (short s in buffer) peak = Math.Max(peak, Math.Abs((int)s));
            return peak;
        }

        Assert.True(PeakAt(0x0F) > PeakAt(0x08));
        Assert.True(PeakAt(0x08) > PeakAt(0x02));
    }

    [Fact]
    public void ThreeChannelsAreLouderThanOne()
    {
        static int PeakWithMixer(byte mixer)
        {
            var ay = Chip();
            for (int ch = 0; ch < 3; ch++)
            {
                Write(ay, ch * 2, (byte)(0x40 + ch * 8));
                Write(ay, 8 + ch, 0x0F);
            }
            Write(ay, 7, mixer);

            short[] buffer = new short[2000];
            ay.Render(buffer, 100000);

            int peak = 0;
            foreach (short s in buffer) peak = Math.Max(peak, Math.Abs((int)s));
            return peak;
        }

        Assert.True(PeakWithMixer(0x38) > PeakWithMixer(0x3E)); // all three vs one
    }

    // ── Noise ────────────────────────────────────────────────────────────────

    [Fact]
    public void NoiseOnlyChannelProducesOutput()
    {
        // Mixer 0x37: channel A noise enabled (bit 3 clear), all tone disabled.
        // Before the noise generator existed this channel was silent, because a
        // disabled tone was treated as silencing the whole channel.
        var ay = Chip();
        Write(ay, 6, 0x05);   // noise period
        Write(ay, 7, 0x37);
        Write(ay, 8, 0x0F);

        short[] buffer = new short[2000];
        ay.Render(buffer, 100000);

        Assert.Contains(buffer, s => s != 0);

        int edges = 0;
        for (int i = 1; i < buffer.Length; i++) if (buffer[i] != buffer[i - 1]) edges++;
        Assert.True(edges > 50, $"noise should switch level constantly, saw {edges} edges");
    }

    [Fact]
    public void NoiseIsNotPeriodicOverAShortWindow()
    {
        // A square wave repeats; the LFSR should not. This distinguishes real
        // noise from a tone wired to the noise bit.
        var ay = Chip();
        Write(ay, 6, 0x01);
        Write(ay, 7, 0x37);
        Write(ay, 8, 0x0F);

        short[] buffer = new short[512];
        ay.Render(buffer, 200000);

        // Count runs of equal samples. A square wave has uniform run lengths;
        // the LFSR produces a spread of them.
        var runLengths = new HashSet<int>();
        int run = 1;
        for (int i = 1; i < buffer.Length; i++)
        {
            if (buffer[i] == buffer[i - 1]) run++;
            else { runLengths.Add(run); run = 1; }
        }

        Assert.True(runLengths.Count >= 3,
            $"noise should give varied run lengths, saw {runLengths.Count}");
    }

    [Fact]
    public void NoiseLfsrNeverLocksAtZero()
    {
        // An all-zero shift register would feed back zero forever and go silent.
        var ay = Chip();
        Write(ay, 6, 0x01);
        Write(ay, 7, 0x37);
        Write(ay, 8, 0x0F);

        short[] buffer = new short[4000];
        for (int i = 0; i < 20; i++) ay.Render(buffer, 100000);

        Assert.Contains(buffer, s => s != 0);
    }

    [Fact]
    public void ToneAndNoiseTogetherAreGatedByBoth()
    {
        // Both sources on one channel are ANDed, so the result is quieter than
        // either alone rather than louder.
        static int NonZeroSamples(byte mixer, byte noisePeriod)
        {
            var ay = Chip();
            Write(ay, 0, 0x40);
            Write(ay, 6, noisePeriod);
            Write(ay, 7, mixer);
            Write(ay, 8, 0x0F);

            short[] buffer = new short[4000];
            ay.Render(buffer, 200000);
            return buffer.Count(s => s != 0);
        }

        int toneOnly = NonZeroSamples(0x3E, 0x05);      // tone A only
        int both     = NonZeroSamples(0x36, 0x05);      // tone A and noise A

        Assert.True(both < toneOnly,
            $"ANDing noise into the tone should cut output, tone={toneOnly} both={both}");
    }

    // ── Envelope ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnvelopeModeUsesTheEnvelopeRatherThanFullVolume()
    {
        // Volume bit 4 hands amplitude to the envelope. It used to be pinned at
        // full volume, so a decaying note played as a flat blast.
        var ay = Chip();
        Write(ay, 0, 0x10);
        Write(ay, 7, 0x3E);
        Write(ay, 11, 0x00);
        Write(ay, 12, 0x02);   // slow-ish envelope
        Write(ay, 13, 0x00);   // shape 0: one ramp down, then silence
        Write(ay, 8, 0x10);    // channel A in envelope mode

        short[] first = new short[1000];
        short[] later = new short[1000];
        ay.Render(first, 200000);
        for (int i = 0; i < 20; i++) ay.Render(later, 200000);

        int peakFirst = first.Max(s => Math.Abs((int)s));
        int peakLater = later.Max(s => Math.Abs((int)s));

        Assert.True(peakFirst > 0, "the envelope should start audible");
        Assert.True(peakLater < peakFirst,
            $"shape 0 should decay to silence, start={peakFirst} end={peakLater}");
    }

    [Theory]
    // Shapes without the continue bit all finish at silence, whichever way they ran.
    [InlineData(0x00)] [InlineData(0x03)] [InlineData(0x04)] [InlineData(0x07)]
    // Shape 9 decays and holds at 0; shape 15 rises then drops to 0.
    [InlineData(0x09)] [InlineData(0x0F)]
    public void ShapesThatEndInSilenceReachZero(byte shape)
    {
        var ay = Chip();
        Write(ay, 11, 0x20);
        Write(ay, 12, 0x00);
        Write(ay, 13, shape);

        RunEnvelope(ay, 40);

        Assert.Equal(0, ay.EnvelopeLevel);
    }

    [Theory]
    // Shape 11 decays then holds at maximum; shape 13 rises and holds there.
    [InlineData(0x0B)] [InlineData(0x0D)]
    public void ShapesThatHoldHighReachMaximum(byte shape)
    {
        var ay = Chip();
        Write(ay, 11, 0x20);
        Write(ay, 12, 0x00);
        Write(ay, 13, shape);

        RunEnvelope(ay, 40);

        Assert.Equal(15, ay.EnvelopeLevel);
    }

    [Theory]
    // The continuing shapes never settle: 8 and 12 repeat, 10 and 14 alternate.
    [InlineData(0x08)] [InlineData(0x0A)] [InlineData(0x0C)] [InlineData(0x0E)]
    public void ContinuingShapesKeepMoving(byte shape)
    {
        var ay = Chip();
        Write(ay, 11, 0x20);
        Write(ay, 12, 0x00);
        Write(ay, 13, shape);

        RunEnvelope(ay, 40);

        var levels = new HashSet<int>();
        for (int i = 0; i < 40; i++)
        {
            RunEnvelope(ay, 1);
            levels.Add(ay.EnvelopeLevel);
        }

        Assert.True(levels.Count > 1,
            $"shape 0x{shape:X} should keep cycling, but sat at a single level");
    }

    [Fact]
    public void RewritingTheShapeRegisterRetriggersTheEnvelope()
    {
        // Music drivers retrigger a note by rewriting register 13 with the same
        // value, so this must not be short-circuited as an unchanged write.
        var ay = Chip();
        Write(ay, 11, 0x20);
        Write(ay, 12, 0x00);
        Write(ay, 13, 0x00);   // decay to silence

        RunEnvelope(ay, 40);
        Assert.Equal(0, ay.EnvelopeLevel);

        Write(ay, 13, 0x00);   // same value again
        Assert.Equal(15, ay.EnvelopeLevel);
    }

    [Fact]
    public void EnvelopeShapeSelectsTheStartingDirection()
    {
        var ay = Chip();
        Write(ay, 11, 0x20);
        Write(ay, 12, 0x00);

        Write(ay, 13, 0x00);   // attack bit clear: starts at maximum, ramps down
        Assert.Equal(15, ay.EnvelopeLevel);

        Write(ay, 13, 0x04);   // attack bit set: starts at zero, ramps up
        Assert.Equal(0, ay.EnvelopeLevel);
    }

    /// <summary>Renders enough silence to advance the envelope by roughly <paramref name="steps"/> steps.</summary>
    private static void RunEnvelope(Ay38912 ay, int steps)
    {
        // Envelope period 0x0020 at the /256 divider: one step is 0x20 * 256
        // AY clocks, and one T-state is one AY clock for this purpose.
        short[] buffer = new short[256];
        ay.Render(buffer, (ulong)steps * 0x20 * 256);
    }

    [Fact]
    public void Reset_ClearsAllRegisters()
    {
        var ay = Chip();
        for (int r = 0; r < 16; r++) Write(ay, r, 0xFF);

        ay.Reset();

        // Check the latch before reading anything back — Read() selects a
        // register as a side effect, so asserting afterwards would see 15.
        Assert.Equal(0, ay.SelectedRegister);

        // 0-13 only: after reset register 7 is 0, so ports A and B are inputs
        // and read as 0xFF rather than the cleared latch.
        for (int r = 0; r < 14; r++) Assert.Equal(0, Read(ay, r));
    }
}
