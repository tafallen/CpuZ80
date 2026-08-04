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
        var ay = Chip();
        for (int r = 0; r < 16; r++) Write(ay, r, (byte)(r * 0x11));

        for (int r = 0; r < 16; r++)
        {
            byte expected = (byte)((r * 0x11) & Ay38912.RegisterMask(r));
            Assert.Equal(expected, Read(ay, r));
        }
    }

    // ── Port decoding ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0xFFFD)] // canonical select port
    [InlineData(0xFFFF)] // A1 high — still A15=1, A14=1
    [InlineData(0xC000)] // A15=1, A14=1
    public void SelectPortRespondsWhenA15AndA14AreHigh(ushort port)
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

    [Fact]
    public void Reset_ClearsAllRegisters()
    {
        var ay = Chip();
        for (int r = 0; r < 16; r++) Write(ay, r, 0xFF);

        ay.Reset();

        // Check the latch before reading anything back — Read() selects a
        // register as a side effect, so asserting afterwards would see 15.
        Assert.Equal(0, ay.SelectedRegister);

        for (int r = 0; r < 16; r++) Assert.Equal(0, Read(ay, r));
    }
}
