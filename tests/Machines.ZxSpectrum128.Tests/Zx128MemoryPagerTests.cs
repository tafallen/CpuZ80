using Xunit;
using CpuZ80.Core;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// Port 0x7FFD — the ZX Spectrum 128's memory paging latch.
/// </summary>
/// <remarks>
/// Write-only, decoded on A15 = 0 and A1 = 0 only, so it answers to any address
/// matching 0xxxxxxx xxxxxx0x rather than just 0x7FFD. Bits 0-2 select the RAM
/// bank at 0xC000, bit 3 the screen bank, bit 4 the ROM, and bit 5 locks paging
/// permanently until reset.
///
/// See docs/zx-spectrum-128.md.
/// </remarks>
public class Zx128MemoryPagerTests
{
    private const ushort PagingPort = 0x7FFD;

    /// <summary>Builds a pager over 8 distinguishable RAM banks and 2 distinguishable ROMs.</summary>
    private static (Zx128MemoryPager Pager, AddressDecoder Bus) Build()
    {
        var bus = new AddressDecoder();

        var banks = new Ram[8];
        for (int i = 0; i < 8; i++)
        {
            banks[i] = new Ram(0x4000);
            // Stamp each bank so reads identify which one is paged in.
            banks[i].Write(0x0000, (byte)(0xB0 + i));
        }

        var roms = new Rom[2];
        for (int i = 0; i < 2; i++)
        {
            byte[] image = new byte[0x4000];
            image[0] = (byte)(0xA0 + i);
            roms[i] = new Rom(image);
        }

        var pager = new Zx128MemoryPager(bus, banks, roms);
        pager.Reset();
        return (pager, bus);
    }

    [Fact]
    public void AfterReset_Bank0AndRom0ArePaged()
    {
        var (pager, bus) = Build();

        Assert.Equal(0, pager.PagedBank);
        Assert.Equal(0, pager.RomIndex);
        Assert.Equal(5, pager.ScreenBank);
        Assert.False(pager.PagingLocked);

        Assert.Equal(0xB0, bus.Read(0xC000)); // bank 0
    }

    [Theory]
    [InlineData(0, 0xB0)]
    [InlineData(1, 0xB1)]
    [InlineData(2, 0xB2)]
    [InlineData(3, 0xB3)]
    [InlineData(4, 0xB4)]
    [InlineData(5, 0xB5)]
    [InlineData(6, 0xB6)]
    [InlineData(7, 0xB7)]
    public void Bits0To2_SelectTheBankAt0xC000(int bank, byte expected)
    {
        var (pager, bus) = Build();

        pager.Out(PagingPort, (byte)bank);

        Assert.Equal(bank, pager.PagedBank);
        Assert.Equal(expected, bus.Read(0xC000));
    }

    [Fact]
    public void Bank5And2_AreAlsoReachableAtTheirFixedWindows()
    {
        // 0x4000 is always bank 5, 0x8000 always bank 2, whatever is at 0xC000.
        var (pager, bus) = Build();

        pager.Out(PagingPort, 0x03); // bank 3 at 0xC000

        Assert.Equal(0xB5, bus.Read(0x4000));
        Assert.Equal(0xB2, bus.Read(0x8000));
        Assert.Equal(0xB3, bus.Read(0xC000));
    }

    [Fact]
    public void PagingTheSameBankIntoBothWindows_SharesStorage()
    {
        // Bank 5 is fixed at 0x4000; paging it to 0xC000 too must alias, not copy.
        var (pager, bus) = Build();

        pager.Out(PagingPort, 0x05);
        bus.Write(0xC001, 0x5A);

        Assert.Equal(0x5A, bus.Read(0x4001));
    }

    [Fact]
    public void Bit4_SelectsTheRom()
    {
        var (pager, bus) = Build();

        Assert.Equal(0xA0, bus.Read(0x0000));

        pager.Out(PagingPort, 0x10);
        Assert.Equal(1, pager.RomIndex);
        Assert.Equal(0xA1, bus.Read(0x0000));

        pager.Out(PagingPort, 0x00);
        Assert.Equal(0, pager.RomIndex);
        Assert.Equal(0xA0, bus.Read(0x0000));
    }

    [Fact]
    public void Rom_IsNotWritable()
    {
        var (_, bus) = Build();

        bus.Write(0x0000, 0xFF);

        Assert.Equal(0xA0, bus.Read(0x0000));
    }

    [Fact]
    public void Bit3_SelectsTheScreenBank()
    {
        var (pager, _) = Build();

        Assert.Equal(5, pager.ScreenBank);

        pager.Out(PagingPort, 0x08);
        Assert.Equal(7, pager.ScreenBank);

        pager.Out(PagingPort, 0x00);
        Assert.Equal(5, pager.ScreenBank);
    }

    [Fact]
    public void ScreenBank_IsIndependentOfThePagedBank()
    {
        // A program can display bank 7 while writing to bank 0 at 0xC000.
        var (pager, bus) = Build();

        pager.Out(PagingPort, 0x08); // screen = 7, paged bank = 0

        Assert.Equal(7, pager.ScreenBank);
        Assert.Equal(0, pager.PagedBank);
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    [Fact]
    public void Bit5_LocksPagingUntilReset()
    {
        var (pager, bus) = Build();

        pager.Out(PagingPort, 0x03);       // bank 3
        pager.Out(PagingPort, 0x20 | 0x01); // bank 1 AND set the lock

        Assert.True(pager.PagingLocked);
        Assert.Equal(1, pager.PagedBank);   // this write still took effect
        Assert.Equal(0xB1, bus.Read(0xC000));

        pager.Out(PagingPort, 0x04);        // ignored
        Assert.Equal(1, pager.PagedBank);
        Assert.Equal(0xB1, bus.Read(0xC000));
    }

    [Fact]
    public void Reset_ClearsTheLock()
    {
        var (pager, bus) = Build();

        pager.Out(PagingPort, 0x20 | 0x02);
        Assert.True(pager.PagingLocked);

        pager.Reset();

        Assert.False(pager.PagingLocked);
        Assert.Equal(0, pager.PagedBank);
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    // ── Partial decoding: A15 = 0 and A1 = 0 ─────────────────────────────────

    [Theory]
    [InlineData(0x7FFD)] // the canonical address
    [InlineData(0x7FFC)] // A0 differs — still A15=0, A1=0
    [InlineData(0x3FFD)] // A14 differs
    [InlineData(0x0000)] // everything low
    [InlineData(0x1234)] // A15=0, A1=0
    public void RespondsToAnyAddressWithA15AndA1Low(ushort port)
    {
        var (pager, bus) = Build();

        pager.Out(port, 0x06);

        Assert.Equal(6, pager.PagedBank);
        Assert.Equal(0xB6, bus.Read(0xC000));
    }

    [Theory]
    [InlineData(0xFFFD)] // A15 high — this is the AY register port
    [InlineData(0x7FFF)] // A1 high
    [InlineData(0xBFFD)] // A15 high — AY data port
    [InlineData(0x0002)] // A1 high
    public void IgnoresAddressesWithA15OrA1High(ushort port)
    {
        var (pager, bus) = Build();

        pager.Out(port, 0x06);

        Assert.Equal(0, pager.PagedBank);
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    [Fact]
    public void In_ReturnsOpenBus_ThePortIsWriteOnly()
    {
        var (pager, _) = Build();

        Assert.Equal(0xFF, pager.In(PagingPort));
    }

    // ── Contention: banks 1, 3, 5, 7 ─────────────────────────────────────────

    [Fact]
    public void FixedWindows_HaveTheExpectedContention()
    {
        var (pager, _) = Build();

        Assert.True(pager.IsContended(0x4000));   // bank 5, always contended
        Assert.True(pager.IsContended(0x7FFF));
        Assert.False(pager.IsContended(0x8000));  // bank 2, never contended
        Assert.False(pager.IsContended(0xBFFF));
        Assert.False(pager.IsContended(0x0000));  // ROM
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void PagedWindow_IsContendedOnlyForOddBanks(int bank, bool contended)
    {
        var (pager, _) = Build();

        pager.Out(PagingPort, (byte)bank);

        Assert.Equal(contended, pager.IsContended(0xC000));
        Assert.Equal(contended, pager.IsContended(0xFFFF));
    }
}
