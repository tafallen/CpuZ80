using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Hardware;

/// <summary>
/// Bank switching: replacing the device behind a range at runtime, which is how
/// the Spectrum 128K, the Amstrad CPC and MSX page ROM and RAM in and out.
/// </summary>
/// <remarks>
/// The cost of a switch must scale with the number of 256-byte pages in the
/// range, not the number of addresses — paging 16K should touch 64 table
/// entries, not 16,384. <see cref="AddressDecoder.Remap"/> replaces rather than
/// merges, which is what banking wants; <see cref="AddressDecoder.Map"/> keeps
/// its existing conflict-policy behaviour.
/// </remarks>
public class AddressDecoderBankingTests
{
    private sealed class StubBus : IBus
    {
        private readonly byte _value;
        public ushort LastOffset;
        public StubBus(byte value) => _value = value;
        public byte Read(ushort offset) { LastOffset = offset; return _value; }
        public void Write(ushort offset, byte value) => LastOffset = offset;
    }

    [Fact]
    public void Remap_ReplacesTheDeviceBehindARange()
    {
        var decoder = new AddressDecoder();
        var romA = new StubBus(0xAA);
        var romB = new StubBus(0xBB);

        decoder.Map(0xC000, 0xFFFF, romA);
        Assert.Equal(0xAA, decoder.Read(0xC000));
        Assert.Equal(0xAA, decoder.Read(0xFFFF));

        decoder.Remap(0xC000, 0xFFFF, romB);
        Assert.Equal(0xBB, decoder.Read(0xC000));
        Assert.Equal(0xBB, decoder.Read(0xFFFF));
    }

    [Fact]
    public void Remap_PreservesOffsetTranslation()
    {
        var decoder = new AddressDecoder();
        var bank = new StubBus(0x11);

        decoder.Remap(0xC000, 0xFFFF, bank);
        decoder.Read(0xC123);

        Assert.Equal(0x0123, bank.LastOffset);
    }

    [Fact]
    public void Remap_DoesNotDisturbNeighbouringRanges()
    {
        var decoder = new AddressDecoder();
        var rom = new StubBus(0x01);
        var ram = new StubBus(0x02);
        var paged = new StubBus(0x03);

        decoder.Map(0x0000, 0x3FFF, rom);
        decoder.Map(0x4000, 0xBFFF, ram);
        decoder.Map(0xC000, 0xFFFF, paged);

        decoder.Remap(0xC000, 0xFFFF, new StubBus(0x04));

        Assert.Equal(0x01, decoder.Read(0x0000));
        Assert.Equal(0x01, decoder.Read(0x3FFF));
        Assert.Equal(0x02, decoder.Read(0x4000));
        Assert.Equal(0x02, decoder.Read(0xBFFF));
        Assert.Equal(0x04, decoder.Read(0xC000));
    }

    [Fact]
    public void Remap_CanBeRepeatedWithoutAccumulating()
    {
        // Banking swaps the same window over and over. Under the LogicalAnd
        // conflict policy, repeated Map() calls would stack devices into a
        // contention bus; Remap must replace instead.
        var decoder = new AddressDecoder(AddressDecoder.ConflictPolicy.LogicalAnd);

        for (int i = 0; i < 50; i++)
            decoder.Remap(0xC000, 0xFFFF, new StubBus(0x7F));

        decoder.Remap(0xC000, 0xFFFF, new StubBus(0x42));
        Assert.Equal(0x42, decoder.Read(0xC000));
    }

    [Fact]
    public void Remap_OverAByteGranularRegion_StillWorks()
    {
        // A page previously split at byte granularity must be fully replaced.
        var decoder = new AddressDecoder();
        var oddByte = new StubBus(0x55);
        var whole = new StubBus(0x66);

        decoder.Map(0x8001, 0x8001, oddByte);
        Assert.Equal(0x55, decoder.Read(0x8001));
        Assert.Equal(0xFF, decoder.Read(0x8000));

        decoder.Remap(0x8000, 0x80FF, whole);
        Assert.Equal(0x66, decoder.Read(0x8000));
        Assert.Equal(0x66, decoder.Read(0x8001));
        Assert.Equal(0x66, decoder.Read(0x80FF));
    }

    [Fact]
    public void Remap_UnalignedRange_LeavesNeighbouringBytesIntact()
    {
        var decoder = new AddressDecoder();
        var background = new StubBus(0x10);
        var window = new StubBus(0x20);

        decoder.Map(0x0000, 0xFFFF, background);
        decoder.Remap(0x1234, 0x5678, window);

        Assert.Equal(0x10, decoder.Read(0x1233));
        Assert.Equal(0x20, decoder.Read(0x1234));
        Assert.Equal(0x20, decoder.Read(0x5678));
        Assert.Equal(0x10, decoder.Read(0x5679));
    }

    [Fact]
    public void Remap_InvalidRange_Throws()
    {
        var decoder = new AddressDecoder();
        Assert.Throws<ArgumentException>(() => decoder.Remap(0x2000, 0x1000, new StubBus(0)));
    }

    [Fact]
    public void Remap_ToNull_UnmapsTheRange()
    {
        // Paging a bank out entirely should leave open bus behind it.
        var decoder = new AddressDecoder();
        decoder.Map(0xC000, 0xFFFF, new StubBus(0xAB));
        Assert.Equal(0xAB, decoder.Read(0xC000));

        decoder.Remap(0xC000, 0xFFFF, null);
        Assert.Equal(0xFF, decoder.Read(0xC000));
    }

    [Fact]
    public void MirroredMapping_SurvivesARemapElsewhere()
    {
        var decoder = new AddressDecoder();
        var rom = new StubBus(0x0F);
        var bank = new StubBus(0xF0);

        // 8K ROM mirrored through the lower 16K.
        decoder.MapMirror(0x0000, 0xC000, 0x1FFF, rom);
        decoder.Map(0xC000, 0xFFFF, bank);

        decoder.Remap(0xC000, 0xFFFF, new StubBus(0x33));

        Assert.Equal(0x0F, decoder.Read(0x0000));
        Assert.Equal(0x0F, decoder.Read(0x2000)); // mirror
        decoder.Read(0x2001);
        Assert.Equal(0x0001, rom.LastOffset);     // masked offset preserved
        Assert.Equal(0x33, decoder.Read(0xC000));
    }
}
