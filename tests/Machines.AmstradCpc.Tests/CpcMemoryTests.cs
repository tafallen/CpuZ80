using Xunit;
using CpuZ80.Core;
using Machines.AmstradCpc;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// The CPC memory map: ROM as an overlay, and the eight RAM configurations.
/// </summary>
/// <remarks>See docs/amstrad-cpc.md §2.3.</remarks>
public class CpcMemoryTests
{
    private static byte[] Rom(byte marker)
    {
        byte[] image = new byte[0x4000];
        image[0] = marker;
        image[0x100] = marker;
        return image;
    }

    private static (CpcMemory Memory, AddressDecoder Bus) Build(bool has128K = true)
    {
        var bus = new AddressDecoder();
        var memory = new CpcMemory(bus, Rom(0xA0), Rom(0xB0), has128K);

        // Stamp each RAM bank so the mapped one is identifiable.
        for (int i = 0; i < memory.Banks.Length; i++) memory.Banks[i].Write(0x0000, (byte)(0xD0 + i));

        return (memory, bus);
    }

    [Fact]
    public void AfterReset_BothRomsArePagedIn()
    {
        var (memory, bus) = Build();

        Assert.True(memory.LowerRomEnabled);
        Assert.True(memory.UpperRomEnabled);
        Assert.Equal(0xA0, bus.Read(0x0000));   // lower ROM
        Assert.Equal(0xB0, bus.Read(0xC000));   // upper ROM
        Assert.Equal(0xD1, bus.Read(0x4000));   // RAM bank 1
        Assert.Equal(0xD2, bus.Read(0x8000));   // RAM bank 2
    }

    // ── ROM as an overlay ────────────────────────────────────────────────────

    [Fact]
    public void WritingUnderTheLowerRom_ReachesTheRamBeneath()
    {
        // This is the whole difference from the Sinclair machines: the firmware
        // keeps variables in the RAM under the ROM, so discarding writes would
        // break it immediately.
        var (memory, bus) = Build();

        bus.Write(0x0001, 0x5A);

        Assert.Equal(0xA0, bus.Read(0x0000));           // still reading ROM
        Assert.Equal(0x5A, memory.Banks[0].Read(0x0001));
    }

    [Fact]
    public void WritingUnderTheUpperRom_ReachesTheRamBeneath()
    {
        var (memory, bus) = Build();

        bus.Write(0xC001, 0x3C);

        Assert.Equal(0xB0, bus.Read(0xC000));
        Assert.Equal(0x3C, memory.Banks[3].Read(0x0001));
    }

    [Fact]
    public void DisablingTheLowerRom_ExposesTheRam()
    {
        var (memory, bus) = Build();

        memory.SetRomEnables(lowerEnabled: false, upperEnabled: true);

        Assert.Equal(0xD0, bus.Read(0x0000));
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    [Fact]
    public void DisablingTheUpperRom_ExposesTheRam()
    {
        var (memory, bus) = Build();

        memory.SetRomEnables(lowerEnabled: true, upperEnabled: false);

        Assert.Equal(0xA0, bus.Read(0x0000));
        Assert.Equal(0xD3, bus.Read(0xC000));
    }

    [Fact]
    public void UpperRomNumberSelectsBetweenFittedRoms()
    {
        var (memory, bus) = Build();
        memory.AddUpperRom(7, Rom(0xC7));    // AMSDOS

        memory.SelectUpperRom(7);
        Assert.Equal(0xC7, bus.Read(0xC000));

        memory.SelectUpperRom(0);
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    [Fact]
    public void SelectingAnAbsentUpperRom_ExposesTheRam()
    {
        // Real hardware has nothing driving the bus, so the RAM shows through.
        var (memory, bus) = Build();

        memory.SelectUpperRom(3);

        Assert.Equal(0xD3, bus.Read(0xC000));
    }

    // ── RAM banking ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 1, 2, 3)]
    [InlineData(1, 0, 1, 2, 7)]
    [InlineData(2, 4, 5, 6, 7)]
    [InlineData(3, 0, 3, 2, 7)]   // the odd one: bank 3 at 0x4000, not bank 1
    [InlineData(4, 0, 4, 2, 3)]
    [InlineData(5, 0, 5, 2, 3)]
    [InlineData(6, 0, 6, 2, 3)]
    [InlineData(7, 0, 7, 2, 3)]
    public void TheEightRamConfigurations(int config, int b0, int b1, int b2, int b3)
    {
        var (memory, bus) = Build();
        memory.SetRomEnables(false, false);   // see the RAM in every window

        memory.SetRamConfig(config);

        Assert.Equal((byte)(0xD0 + b0), bus.Read(0x0000));
        Assert.Equal((byte)(0xD0 + b1), bus.Read(0x4000));
        Assert.Equal((byte)(0xD0 + b2), bus.Read(0x8000));
        Assert.Equal((byte)(0xD0 + b3), bus.Read(0xC000));
    }

    [Fact]
    public void ConfigThreeIsNotDerivableFromTheOthers()
    {
        // Guards specifically against a clever formula: every other config puts
        // bank 1 or a second-page bank at 0x4000, and config 3 puts base bank 3
        // there. A pattern that fits the other seven gets this one wrong.
        var (memory, _) = Build();

        memory.SetRamConfig(3);

        Assert.Equal(3, memory.BankAt(1));
        Assert.NotEqual(1, memory.BankAt(1));
    }

    [Fact]
    public void A464HasOnlyTheBase64K()
    {
        var (memory, _) = Build(has128K: false);

        Assert.False(memory.Has128K);
        Assert.Equal(4, memory.Banks.Length);

        // A configuration reaching into the second 64K has nothing to reach.
        memory.SetRamConfig(2);
        for (int window = 0; window < 4; window++)
        {
            Assert.InRange(memory.BankAt(window), 0, 3);
        }
    }

    [Fact]
    public void Reset_RestoresBothRomsAndConfigZero()
    {
        var (memory, bus) = Build();

        memory.SetRomEnables(false, false);
        memory.SetRamConfig(2);

        memory.Reset();

        Assert.Equal(0, memory.RamConfig);
        Assert.Equal(0xA0, bus.Read(0x0000));
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    // ── ROM sizing ───────────────────────────────────────────────────────────

    [Fact]
    public void RomsMustBe16K()
    {
        var bus = new AddressDecoder();
        Assert.Throws<ArgumentException>(() => new CpcMemory(bus, new byte[0x2000], Rom(0xB0)));
        Assert.Throws<ArgumentException>(() => new CpcMemory(bus, Rom(0xA0), new byte[0x8000]));
    }

    [Fact]
    public void AHeaderedRomImageIsAccepted()
    {
        // A 16K ROM with an AMSDOS header on it is 16,512 bytes and must load.
        byte[] headered = new byte[0x4000 + AmsdosHeader.Size];
        headered[0x12] = 0x02;
        headered[0x18] = 0x00;
        headered[0x19] = 0x40;
        headered[AmsdosHeader.Size] = 0xA5;

        var bus = new AddressDecoder();
        var memory = new CpcMemory(bus, headered, Rom(0xB0));

        Assert.Equal(0xA5, bus.Read(0x0000));
        Assert.NotNull(memory);
    }
}
