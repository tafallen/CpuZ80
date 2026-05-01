using Machines.Common;
using Machines.Zx80;
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
        // When ITapeDevice.ReadBit() returns false (pulse present), bit 6 of IN is low.
        var tape = new StubTape(readBit: false);
        var machine = new Zx80Machine(NopRom(), tape: tape);
        byte result = machine.ReadPort(0xFEFE);
        Assert.Equal(0, (result >> 6) & 1);
    }

    [Fact]
    public void Tape_ReadBit_HighOnSilence()
    {
        // When ITapeDevice.ReadBit() returns true (silence), bit 6 of IN is high.
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

    // ── Zx80TapeAdapter pulse encoding (tests 4–5) ───────────────────────────

    [Fact]
    public void TapeAdapter_ZeroBit_Generates4PulseHighLowPairs()
    {
        // A byte of 0x00: all 8 bits are 0. Each 0-bit = 4 × (HIGH, LOW).
        // The first bit (MSB) is 0, so ReadBit() should produce 4 HIGH+LOW pairs.
        var adapter = new Zx80TapeAdapter();
        adapter.Load(new MemoryStream([0x00]));

        // 0 bit → 4 pulses = 8 ReadBit() calls: true,false,true,false,true,false,true,false
        var states = new List<bool>();
        for (int i = 0; i < 8; i++)
            states.Add(adapter.ReadBit());

        Assert.Equal([true, false, true, false, true, false, true, false], states);
    }

    [Fact]
    public void TapeAdapter_OneBit_Generates9PulseHighLowPairs()
    {
        // A byte of 0x80 (MSB=1): first bit is 1 → 9 × (HIGH, LOW) = 18 ReadBit() calls.
        var adapter = new Zx80TapeAdapter();
        adapter.Load(new MemoryStream([0x80]));

        var states = new List<bool>();
        for (int i = 0; i < 18; i++)
            states.Add(adapter.ReadBit());

        // 9 pulses = 9 true, 9 false, interleaved
        for (int p = 0; p < 9; p++)
        {
            Assert.True(states[p * 2],      $"Pulse {p} HIGH should be true");
            Assert.False(states[p * 2 + 1], $"Pulse {p} LOW should be false");
        }
    }

    [Fact]
    public void TapeAdapter_AfterAllPulses_ReturnsSilence()
    {
        // Once all data is exhausted, ReadBit() returns true (silence = no signal).
        var adapter = new Zx80TapeAdapter();
        adapter.Load(new MemoryStream([])); // empty tape
        Assert.True(adapter.ReadBit());
    }

    // ── Round-trip: verify total signal length for a known byte (test 6 adapted) ─

    [Fact]
    public void TapeAdapter_RoundTrip_CorrectSignalLength()
    {
        // 0xA5 = 10100101 — bits MSB-first: 1,0,1,0,0,1,0,1
        // Signal lengths: 1→18, 0→8 each.
        // Total = 18+8+18+8+8+18+8+18 = 104 states.
        //
        // Pulse pairs always alternate HIGH(true)/LOW(false); two consecutive trues
        // can ONLY appear once the signal is exhausted (silence). We use this property
        // to count states without needing to know bit boundaries.
        const byte testByte = 0xA5;
        const int  expected  = 104; // 4×18 + 4×8

        var adapter = new Zx80TapeAdapter();
        adapter.Load(new MemoryStream([testByte]));

        Assert.Equal(expected, CountSignalStates(adapter));
    }

    [Fact]
    public void TapeAdapter_RoundTrip_AllZerosByte()
    {
        // 0x00 = 8 zero-bits → 8 × 8 states = 64 total.
        var adapter = new Zx80TapeAdapter();
        adapter.Load(new MemoryStream([0x00]));
        Assert.Equal(64, CountSignalStates(adapter));
    }

    [Fact]
    public void TapeAdapter_RoundTrip_AllOnesByte()
    {
        // 0xFF = 8 one-bits → 8 × 18 states = 144 total.
        var adapter = new Zx80TapeAdapter();
        adapter.Load(new MemoryStream([0xFF]));
        Assert.Equal(144, CountSignalStates(adapter));
    }

    // Count ReadBit() signal states until silence (two consecutive trues).
    // Pulse pairs are always HIGH(true)/LOW(false). When a HIGH is followed by another
    // HIGH, the data is exhausted. We peek on each HIGH to avoid counting silence.
    private static int CountSignalStates(Zx80TapeAdapter adapter)
    {
        int count = 0;
        while (true)
        {
            bool state = adapter.ReadBit();
            if (!state) { count++; continue; }       // LOW: always part of signal
            bool next = adapter.ReadBit();
            if (!next) { count += 2; continue; }     // HIGH+LOW: real pulse pair
            break;                                    // HIGH+HIGH: silence — stop
        }
        return count;
    }

    // ── Stub helpers ─────────────────────────────────────────────────────────

    private sealed class StubTape : ITapeDevice
    {
        private readonly bool _readBit;
        public bool LastWrittenBit { get; private set; }

        public StubTape(bool readBit) => _readBit = readBit;

        public bool ReadBit()           => _readBit;
        public void WriteBit(bool bit)  => LastWrittenBit = bit;
        public void Load(Stream data)   { }
    }
}
