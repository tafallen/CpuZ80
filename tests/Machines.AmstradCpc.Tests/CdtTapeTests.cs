using Xunit;
using Machines.AmstradCpc;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// Loading <c>.CDT</c> tape images.
/// </summary>
/// <remarks>
/// <c>.CDT</c> is TZX under another extension. Every timing in the file is in
/// Spectrum T-states at 3.5 MHz and has to be scaled for the CPC's 4 MHz — the
/// single detail most likely to make some tapes load and others fail for no
/// visible reason, so it is measured here rather than assumed.
/// </remarks>
public class CdtTapeTests
{
    /// <summary>Builds .CDT files byte by byte, since we have no real ones.</summary>
    private static class Cdt
    {
        public static List<byte> Header()
        {
            var bytes = new List<byte>();
            bytes.AddRange("ZXTape!"u8.ToArray());
            bytes.Add(0x1A);
            bytes.Add(1);      // major
            bytes.Add(20);     // minor
            return bytes;
        }

        public static void Word(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value & 0xFF));
            bytes.Add((byte)((value >> 8) & 0xFF));
        }

        public static void Triple(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value & 0xFF));
            bytes.Add((byte)((value >> 8) & 0xFF));
            bytes.Add((byte)((value >> 16) & 0xFF));
        }

        /// <summary>Block 0x11, the one a CPC tape normally uses.</summary>
        public static void Turbo(
            List<byte> bytes, byte[] data,
            int pilot = 2168, int sync1 = 667, int sync2 = 735,
            int zero = 855, int one = 1710, int pilotCount = 16,
            int usedBits = 8, int pause = 0)
        {
            bytes.Add(0x11);
            Word(bytes, pilot);
            Word(bytes, sync1);
            Word(bytes, sync2);
            Word(bytes, zero);
            Word(bytes, one);
            Word(bytes, pilotCount);
            bytes.Add((byte)usedBits);
            Word(bytes, pause);
            Triple(bytes, data.Length);
            bytes.AddRange(data);
        }

        public static void PureTone(List<byte> bytes, int length, int count)
        {
            bytes.Add(0x12);
            Word(bytes, length);
            Word(bytes, count);
        }

        public static void PulseSequence(List<byte> bytes, params int[] lengths)
        {
            bytes.Add(0x13);
            bytes.Add((byte)lengths.Length);
            foreach (int l in lengths) Word(bytes, l);
        }

        public static void PureData(
            List<byte> bytes, byte[] data,
            int zero = 855, int one = 1710, int usedBits = 8, int pause = 0)
        {
            bytes.Add(0x14);
            Word(bytes, zero);
            Word(bytes, one);
            bytes.Add((byte)usedBits);
            Word(bytes, pause);
            Triple(bytes, data.Length);
            bytes.AddRange(data);
        }

        public static void Pause(List<byte> bytes, int milliseconds)
        {
            bytes.Add(0x20);
            Word(bytes, milliseconds);
        }

        public static void TextDescription(List<byte> bytes, string text)
        {
            bytes.Add(0x30);
            bytes.Add((byte)text.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
        }

        public static MemoryStream Stream(List<byte> bytes) => new([.. bytes]);
    }

    /// <summary>
    /// One TZX T-state count in host T-states.
    /// </summary>
    /// <remarks>
    /// Each pulse is scaled and truncated on its own, so a total has to be
    /// summed from scaled pulses rather than scaled from a total — the two
    /// differ by a T-state or so per pulse.
    /// </remarks>
    private static long Scale(int tzx, int clockHz = 4_000_000) => (long)tzx * clockHz / 3_500_000;

    private static CdtTape Load(List<byte> bytes, int clockHz = 4_000_000)
    {
        var tape = new CdtTape(clockHz);
        tape.Load(Cdt.Stream(bytes));
        return tape;
    }

    // ── The header ───────────────────────────────────────────────────────────

    [Fact]
    public void AFileThatIsNotATapeIsRejected()
    {
        var tape = new CdtTape();

        var ex = Assert.Throws<InvalidDataException>(() => tape.Load(new MemoryStream(new byte[64])));
        Assert.Contains("ZXTape!", ex.Message);
    }

    [Fact]
    public void AnEmptyTapeLoadsAndPlaysNothing()
    {
        var tape = Load(Cdt.Header());

        Assert.True(tape.AtEnd);
        Assert.False(tape.ReadBit(0));
    }

    // ── Timing: the detail that decides whether tapes load ────────────────────

    [Fact]
    public void TimingsAreScaledFromTheSpectrumClockToTheHosts()
    {
        // A 3500-T-state pulse at 3.5 MHz is a millisecond, so on a 4 MHz CPC it
        // must last 4000 T-states. Playing it unscaled makes every pulse about
        // 14% short — inside what a forgiving loader tolerates and outside what
        // a tight one does.
        var bytes = Cdt.Header();
        Cdt.PureTone(bytes, 3500, 1);

        var tape = Load(bytes);

        Assert.Equal(4000UL, tape.LengthInTStates);
    }

    [Fact]
    public void AHostAtTheSpectrumClockNeedsNoScaling()
    {
        var bytes = Cdt.Header();
        Cdt.PureTone(bytes, 3500, 1);

        var tape = Load(bytes, clockHz: 3_500_000);

        Assert.Equal(3500UL, tape.LengthInTStates);
    }

    [Fact]
    public void APauseIsMeasuredInMilliseconds()
    {
        var bytes = Cdt.Header();
        Cdt.Pause(bytes, 10);

        var tape = Load(bytes);

        // 10 ms at 4 MHz.
        Assert.Equal(40_000UL, tape.LengthInTStates);
    }

    // ── Pulse blocks ─────────────────────────────────────────────────────────

    [Fact]
    public void APureToneAlternatesItsLevel()
    {
        // Each pulse is a half-wave: the level alternates from one to the next.
        var bytes = Cdt.Header();
        Cdt.PureTone(bytes, 3500, 4);

        var tape = Load(bytes);

        Assert.False(tape.ReadBit(1000));        // first pulse
        Assert.True(tape.ReadBit(5000));         // second
        Assert.False(tape.ReadBit(9000));        // third
    }

    [Fact]
    public void APulseSequenceUsesEachGivenLength()
    {
        var bytes = Cdt.Header();
        Cdt.PulseSequence(bytes, 3500, 7000, 3500);

        var tape = Load(bytes);

        // 1ms + 2ms + 1ms at 4 MHz.
        Assert.Equal((ulong)(Scale(3500) + Scale(7000) + Scale(3500)), tape.LengthInTStates);
    }

    // ── Data blocks ──────────────────────────────────────────────────────────

    [Fact]
    public void ATurboBlockPlaysPilotSyncAndData()
    {
        var bytes = Cdt.Header();
        Cdt.Turbo(bytes, [0x00], pilotCount: 16);

        var tape = Load(bytes);

        // 16 pilot pulses, two sync pulses, then 8 bits of two pulses each.
        long expected = 16 * Scale(2168) + Scale(667) + Scale(735) + 8 * 2 * Scale(855);
        Assert.Equal((ulong)expected, tape.LengthInTStates);
        Assert.Equal(1, tape.DataBlockCount);
    }

    [Fact]
    public void AOneBitIsLongerThanAZeroBit()
    {
        static ulong LengthOf(byte value)
        {
            var bytes = Cdt.Header();
            Cdt.PureData(bytes, [value]);
            return Load(bytes).LengthInTStates;
        }

        Assert.True(LengthOf(0xFF) > LengthOf(0x00));
    }

    [Fact]
    public void TheLastBytesUnusedBitsAreNotPlayed()
    {
        // Playing the padding appends bits the loader never expects.
        var full = Cdt.Header();
        Cdt.PureData(full, [0x00, 0x00], usedBits: 8);

        var partial = Cdt.Header();
        Cdt.PureData(partial, [0x00, 0x00], usedBits: 3);

        ulong fullLength = Load(full).LengthInTStates;
        ulong partialLength = Load(partial).LengthInTStates;

        // Five fewer bits, each two pulses of 855.
        long difference = 5 * 2 * Scale(855);
        Assert.Equal(fullLength - (ulong)difference, partialLength);
    }

    [Fact]
    public void AStandardSpeedBlockPicksItsPilotFromTheFlagByte()
    {
        // A header block gets the long pilot and a data block the short one,
        // decided by the first byte's top bit.
        static ulong LengthOf(byte flag)
        {
            var bytes = Cdt.Header();
            bytes.Add(0x10);
            Cdt.Word(bytes, 0);          // pause
            Cdt.Word(bytes, 1);          // length
            bytes.Add(flag);
            return Load(bytes).LengthInTStates;
        }

        Assert.True(LengthOf(0x00) > LengthOf(0xFF));
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void MetadataBlocksAreSkippedRatherThanRejected()
    {
        // A tape that describes itself is still a valid tape.
        var bytes = Cdt.Header();
        Cdt.TextDescription(bytes, "Test Game");
        Cdt.PureTone(bytes, 3500, 1);

        var tape = Load(bytes);

        Assert.Equal(4000UL, tape.LengthInTStates);
        Assert.Contains("Test Game", tape.Descriptions);
    }

    [Fact]
    public void AnUnknownBlockIsRefusedRatherThanSkipped()
    {
        // Block lengths are not self-describing, so skipping an unrecognised one
        // means guessing where it ends. Guessing wrong plays the rest of the
        // file as noise, which looks like a broken tape rather than a
        // limitation.
        var bytes = Cdt.Header();
        bytes.Add(0x99);

        var tape = new CdtTape();

        var ex = Assert.Throws<InvalidDataException>(() => tape.Load(Cdt.Stream(bytes)));
        Assert.Contains("0x99", ex.Message);
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    [Fact]
    public void PlaybackStartsFromWheneverTheMotorFirstReads()
    {
        // The machine has been running for a while before the motor starts.
        var bytes = Cdt.Header();
        Cdt.PureTone(bytes, 3500, 2);

        var tape = Load(bytes);

        const ulong origin = 5_000_000;
        Assert.False(tape.ReadBit(origin));
        Assert.True(tape.ReadBit(origin + 5000));
    }

    [Fact]
    public void AFinishedTapeReadsLow()
    {
        var bytes = Cdt.Header();
        Cdt.PureTone(bytes, 3500, 2);

        var tape = Load(bytes);
        tape.ReadBit(0);

        Assert.False(tape.ReadBit(tape.LengthInTStates + 1));
        Assert.True(tape.AtEnd);
    }

    [Fact]
    public void RewindPlaysItAgain()
    {
        var bytes = Cdt.Header();
        Cdt.PureTone(bytes, 3500, 2);

        var tape = Load(bytes);
        tape.ReadBit(0);
        tape.ReadBit(tape.LengthInTStates + 1);
        Assert.True(tape.AtEnd);

        tape.Rewind();

        Assert.False(tape.AtEnd);
        Assert.False(tape.ReadBit(1_000_000));
    }

    [Fact]
    public void ThePulseTrainRunsInOrderOverTime()
    {
        // Sampled continuously, the tape must produce the alternating edges a
        // loader counts — not just the right total length.
        var bytes = Cdt.Header();
        Cdt.Turbo(bytes, [0xA5], pilotCount: 8);

        var tape = Load(bytes);

        int edges = 0;
        bool last = tape.ReadBit(0);
        for (ulong t = 0; t < tape.LengthInTStates; t += 100)
        {
            bool level = tape.ReadBit(t);
            if (level != last) edges++;
            last = level;
        }

        // 8 pilot + 2 sync + 16 data pulses is 26 pulses, so 25 edges between
        // them, give or take the sampling interval.
        Assert.InRange(edges, 20, 30);
    }
}
