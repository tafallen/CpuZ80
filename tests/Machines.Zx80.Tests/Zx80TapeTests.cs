using Machines.Common;
using Machines.Zx80;
using Machines.Sinclair.Common;
using Xunit;

namespace Machines.Zx80.Tests;

public class Zx80TapeTests
{
    private static byte[] NopRom() => new byte[0x1000];

    // ── Port routing (tests 1–3) ──────────────────────────────────────────────

    [Fact]
    public void Tape_NoDevice_EarBit6High()
    {
        // Without a tape device, IN 0xFE bit 6 must be high (no signal = silence).
        var machine = new Zx80Machine(NopRom());
        byte result = machine.ReadPort(0xFEFE);
        Assert.Equal(1, (result >> 6) & 1);
    }

    [Fact]
    public void Tape_ReadBit_LowOnPulse()
    {
        // When ITapeDevice.ReadBit(0) returns false (pulse present), bit 6 of IN is low.
        var tape = new StubTape(readBit: false);
        var machine = new Zx80Machine(NopRom(), tape: tape);
        byte result = machine.ReadPort(0xFEFE);
        Assert.Equal(0, (result >> 6) & 1);
    }

    [Fact]
    public void Tape_ReadBit_HighOnSilence()
    {
        // When ITapeDevice.ReadBit(0) returns true (silence), bit 6 of IN is high.
        var tape = new StubTape(readBit: true);
        var machine = new Zx80Machine(NopRom(), tape: tape);
        byte result = machine.ReadPort(0xFEFE);
        Assert.Equal(1, (result >> 6) & 1);
    }

    [Fact]
    public void Tape_WriteBit_ForwardedToDevice()
    {
        // OUT 0xFE with bit 3 set: WriteBit(true) must be called on the device.
        var tape = new StubTape(readBit: true);
        var machine = new Zx80Machine(NopRom(), tape: tape);
        machine.WritePort(0xFEFE, 0x08); // bit 3 set
        Assert.True(tape.LastWrittenBit);
    }

    [Fact]
    public void Tape_WriteBit_ClearBit_ForwardedFalse()
    {
        var tape = new StubTape(readBit: true);
        var machine = new Zx80Machine(NopRom(), tape: tape);
        machine.WritePort(0xFEFE, 0x00); // bit 3 clear
        Assert.False(tape.LastWrittenBit);
    }

    // ── SinclairTapeAdapter: timed pulse playback ───────────────────────────────
    //
    // These replace an earlier set that called ReadBit(0) repeatedly and expected
    // one signal level per call. That model made the pulse rate depend on how
    // often the ULA happened to poll rather than on elapsed time, so it could
    // never have loaded a real file — and no test ever loaded one, so nothing
    // caught it.

    private const int HalfPulse = 487;    // 150us at 3.25MHz
    private const int BitGap = 4225;      // 1300us

    [Fact]
    public void TapeAdapter_PulsesAlternateAtTheRealRate()
    {
        var adapter = new SinclairTapeAdapter();
        adapter.Load(new MemoryStream([0x00]));   // first bit is a 0: four pulses

        // Sampling in the middle of each half-pulse gives its level.
        Assert.True(adapter.ReadBit((ulong)(HalfPulse * 0.5)));
        Assert.False(adapter.ReadBit((ulong)(HalfPulse * 1.5)));
        Assert.True(adapter.ReadBit((ulong)(HalfPulse * 2.5)));
        Assert.False(adapter.ReadBit((ulong)(HalfPulse * 3.5)));
    }

    [Fact]
    public void TapeAdapter_HoldsItsLevelBetweenTransitions()
    {
        // The whole point of timing it: sampling twice inside one half-pulse
        // must give the same answer both times.
        var adapter = new SinclairTapeAdapter();
        adapter.Load(new MemoryStream([0x00]));

        Assert.True(adapter.ReadBit(10));
        Assert.True(adapter.ReadBit(20));
        Assert.True(adapter.ReadBit((ulong)(HalfPulse - 1)));
    }

    [Theory]
    [InlineData(0x00, 4)]   // every bit 0: four pulses each
    [InlineData(0xFF, 9)]   // every bit 1: nine pulses each
    public void TapeAdapter_BitsUseTheRightPulseCount(byte value, int pulsesPerBit)
    {
        var adapter = new SinclairTapeAdapter();
        adapter.Load(new MemoryStream([value]));

        // Eight bits, each pulsesPerBit pulses of two half-pulses, plus a gap.
        ulong expected = (ulong)(8 * (pulsesPerBit * 2 * HalfPulse + BitGap));

        Assert.Equal(expected, adapter.LengthInTStates);
    }

    [Fact]
    public void TapeAdapter_RunsOutIntoSilence()
    {
        var adapter = new SinclairTapeAdapter();
        adapter.Load(new MemoryStream([0x00]));

        // Start it playing first: the origin is set by the first read, so a
        // single read at the end would just start the tape there.
        adapter.ReadBit(0);

        Assert.True(adapter.ReadBit(adapter.LengthInTStates + 1));
        Assert.True(adapter.AtEnd);
    }

    [Fact]
    public void TapeAdapter_EmptyTapeIsSilent()
    {
        var adapter = new SinclairTapeAdapter();
        adapter.Load(new MemoryStream([]));

        Assert.True(adapter.ReadBit(0));
        Assert.True(adapter.AtEnd);
    }

    [Fact]
    public void TapeAdapter_PlaybackStartsFromWheneverItIsFirstRead()
    {
        // The CPU clock is already running by the time the ROM starts loading,
        // so the tape cannot assume it begins at zero.
        var adapter = new SinclairTapeAdapter();
        adapter.Load(new MemoryStream([0x00]));

        const ulong origin = 1_000_000;
        Assert.True(adapter.ReadBit(origin));
        Assert.False(adapter.ReadBit(origin + (ulong)(HalfPulse * 1.5)));
    }

    // ── SinclairTapeAdapter: recording ──────────────────────────────────────────

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0xA5)]
    [InlineData(0x3C)]
    public void TapeAdapter_RecordsWhatTheMachineSaves(byte value)
    {
        // Saving is the same encoding in reverse, so playing a byte's own pulse
        // train into the recorder must give the byte back.
        var adapter = new SinclairTapeAdapter();
        ulong t = 0;

        adapter.WriteBit(false, t);

        for (int bit = 7; bit >= 0; bit--)
        {
            int pulses = ((value >> bit) & 1) != 0 ? 9 : 4;
            for (int p = 0; p < pulses; p++)
            {
                adapter.WriteBit(true, t);
                t += HalfPulse;
                adapter.WriteBit(false, t);
                t += HalfPulse;
            }

            // The gap that ends the bit.
            t += BitGap;
            adapter.WriteBit(false, t);
        }

        Assert.Equal([value], adapter.RecordedBytes);
    }

    [Fact]
    public void TapeAdapter_SaveWritesTheRecordedBytes()
    {
        var adapter = new SinclairTapeAdapter();
        RecordByte(adapter, 0x5A, 0);

        var stream = new MemoryStream();
        adapter.Save(stream);

        Assert.Equal([0x5A], stream.ToArray());
    }

    [Fact]
    public void TapeAdapter_UntimedWritesAreIgnoredRatherThanGuessed()
    {
        // A level with no timestamp cannot be decoded. Guessing a duration would
        // produce plausible but wrong data, which is worse than nothing.
        var adapter = new SinclairTapeAdapter();

        for (int i = 0; i < 200; i++) adapter.WriteBit(i % 2 == 0);

        Assert.Empty(adapter.RecordedBytes);
    }

    [Fact]
    public void TapeAdapter_ClearRecordingDiscardsTheCapture()
    {
        var adapter = new SinclairTapeAdapter();
        RecordByte(adapter, 0x5A, 0);
        Assert.NotEmpty(adapter.RecordedBytes);

        adapter.ClearRecording();

        Assert.Empty(adapter.RecordedBytes);
    }

    /// <summary>Plays one byte's pulse train into the recorder.</summary>
    private static ulong RecordByte(SinclairTapeAdapter adapter, byte value, ulong t)
    {
        adapter.WriteBit(false, t);

        for (int bit = 7; bit >= 0; bit--)
        {
            int pulses = ((value >> bit) & 1) != 0 ? 9 : 4;
            for (int p = 0; p < pulses; p++)
            {
                adapter.WriteBit(true, t);
                t += HalfPulse;
                adapter.WriteBit(false, t);
                t += HalfPulse;
            }
            t += BitGap;
            adapter.WriteBit(false, t);
        }

        return t;
    }


    // ── Stub helpers ─────────────────────────────────────────────────────────

    private sealed class StubTape : ITapeDevice
    {
        private readonly bool _readBit;
        public bool LastWrittenBit { get; private set; }

        public StubTape(bool readBit) => _readBit = readBit;

        public bool ReadBit(ulong currentTState) => _readBit;
        public void WriteBit(bool bit)  => LastWrittenBit = bit;
        public void Load(Stream data)   { }
    }
}
