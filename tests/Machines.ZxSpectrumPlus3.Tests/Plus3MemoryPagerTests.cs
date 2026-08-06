using Xunit;
using CpuZ80.Core;
using Machines.ZxSpectrumPlus3;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// The +2A/+3 memory pager: ports 0x7FFD and 0x1FFD, four ROMs, and the all-RAM
/// "special" configurations.
/// </summary>
/// <remarks>
/// See docs/zx-spectrum-plus3.md. Three things differ from the 128 in ways that
/// extrapolating would get wrong: there are four ROMs (the high select bit lives
/// in 0x1FFD), the contended banks are 4-7 rather than the odd ones, and special
/// mode puts RAM at 0x0000 so the bottom 16K becomes writable.
/// </remarks>
public class Plus3MemoryPagerTests
{
    private const ushort Port7ffd = 0x7FFD;
    private const ushort Port1ffd = 0x1FFD;

    private static (Plus3MemoryPager Pager, AddressDecoder Bus) Build()
    {
        var bus = new AddressDecoder();

        var banks = new Ram[8];
        for (int i = 0; i < 8; i++)
        {
            banks[i] = new Ram(0x4000);
            banks[i].Write(0x0000, (byte)(0xB0 + i));   // stamp: which bank is here?
        }

        var roms = new Rom[4];
        for (int i = 0; i < 4; i++)
        {
            byte[] image = new byte[0x4000];
            image[0] = (byte)(0xA0 + i);
            roms[i] = new Rom(image);
        }

        var pager = new Plus3MemoryPager(bus, banks, roms);
        pager.Reset();
        return (pager, bus);
    }

    // ── Normal mode ──────────────────────────────────────────────────────────

    [Fact]
    public void AfterReset_Rom0AndBank0ArePaged()
    {
        var (pager, bus) = Build();

        Assert.False(pager.SpecialMode);
        Assert.Equal(0, pager.RomIndex);
        Assert.Equal(0, pager.PagedBank);
        Assert.Equal(5, pager.ScreenBank);
        Assert.Equal(0xA0, bus.Read(0x0000));
        Assert.Equal(0xB5, bus.Read(0x4000));
        Assert.Equal(0xB2, bus.Read(0x8000));
        Assert.Equal(0xB0, bus.Read(0xC000));
    }

    [Theory]
    // 0x7FFD bit 4 is the LOW bit of the ROM index; 0x1FFD bit 2 is the HIGH bit.
    [InlineData(0x00, 0x00, 0, 0xA0)]
    [InlineData(0x10, 0x00, 1, 0xA1)]
    [InlineData(0x00, 0x04, 2, 0xA2)]
    [InlineData(0x10, 0x04, 3, 0xA3)]
    public void RomIndexCombinesBothPorts(byte v7ffd, byte v1ffd, int expectedRom, byte marker)
    {
        var (pager, bus) = Build();

        pager.Out(Port7ffd, v7ffd);
        pager.Out(Port1ffd, v1ffd);

        Assert.Equal(expectedRom, pager.RomIndex);
        Assert.Equal(marker, bus.Read(0x0000));
    }

    [Fact]
    public void RomIsNotWritableInNormalMode()
    {
        var (_, bus) = Build();
        bus.Write(0x0000, 0xFF);
        Assert.Equal(0xA0, bus.Read(0x0000));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void PagedWindowFollows7ffd(int bank)
    {
        var (pager, bus) = Build();
        pager.Out(Port7ffd, (byte)bank);
        Assert.Equal((byte)(0xB0 + bank), bus.Read(0xC000));
    }

    [Fact]
    public void ScreenBankStillFollowsBit3()
    {
        var (pager, _) = Build();

        pager.Out(Port7ffd, 0x08);
        Assert.Equal(7, pager.ScreenBank);

        pager.Out(Port7ffd, 0x00);
        Assert.Equal(5, pager.ScreenBank);
    }

    [Fact]
    public void PagingLockStillApplies()
    {
        var (pager, bus) = Build();

        pager.Out(Port7ffd, 0x20 | 0x03);
        Assert.True(pager.PagingLocked);
        Assert.Equal(0xB3, bus.Read(0xC000));

        pager.Out(Port7ffd, 0x01);
        Assert.Equal(0xB3, bus.Read(0xC000));   // ignored
    }

    // ── Special (all-RAM) mode ───────────────────────────────────────────────

    [Theory]
    // 0x1FFD value, then the banks expected at 0x0000/0x4000/0x8000/0xC000.
    [InlineData(0x01, 0, 1, 2, 3)]   // bit0 set, config 0
    [InlineData(0x03, 4, 5, 6, 7)]   // config 1 (bit 1)
    [InlineData(0x05, 4, 5, 6, 3)]   // config 2 (bit 2)
    [InlineData(0x07, 4, 7, 6, 3)]   // config 3 (bits 1+2)
    public void SpecialModeMapsTheDocumentedConfigurations(byte v1ffd, int b0, int b1, int b2, int b3)
    {
        var (pager, bus) = Build();

        pager.Out(Port1ffd, v1ffd);

        Assert.True(pager.SpecialMode);
        Assert.Equal((byte)(0xB0 + b0), bus.Read(0x0000));
        Assert.Equal((byte)(0xB0 + b1), bus.Read(0x4000));
        Assert.Equal((byte)(0xB0 + b2), bus.Read(0x8000));
        Assert.Equal((byte)(0xB0 + b3), bus.Read(0xC000));
    }

    [Fact]
    public void SpecialMode_MakesTheBottom16KWritable()
    {
        // This is the whole point of special mode: CP/M cannot run with ROM at
        // 0x0000.
        var (pager, bus) = Build();

        pager.Out(Port1ffd, 0x01);
        bus.Write(0x0001, 0x5A);

        Assert.Equal(0x5A, bus.Read(0x0001));
    }

    [Fact]
    public void LeavingSpecialMode_RestoresTheRom()
    {
        var (pager, bus) = Build();

        pager.Out(Port1ffd, 0x01);
        Assert.Equal(0xB0, bus.Read(0x0000));

        pager.Out(Port1ffd, 0x00);
        Assert.False(pager.SpecialMode);
        Assert.Equal(0xA0, bus.Read(0x0000));
    }

    [Fact]
    public void SpecialMode_StillHonoursTheScreenBit()
    {
        var (pager, _) = Build();

        pager.Out(Port7ffd, 0x08);   // shadow screen
        pager.Out(Port1ffd, 0x01);   // special mode

        Assert.True(pager.SpecialMode);
        Assert.Equal(7, pager.ScreenBank);
    }

    // ── Port decoding ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x1FFD)]  // canonical
    [InlineData(0x1000)]  // A12 set, A1 clear, A13-15 clear
    [InlineData(0x1FFC)]  // A0 differs
    public void Port1ffdRespondsWhenA12SetAndA13To15AndA1Clear(ushort port)
    {
        var (pager, _) = Build();
        pager.Out(port, 0x01);
        Assert.True(pager.SpecialMode);
    }

    [Theory]
    [InlineData(0x1FFF)]  // A1 set
    [InlineData(0x0FFD)]  // A12 clear
    [InlineData(0x3FFD)]  // A13 set
    [InlineData(0xFFFD)]  // A15 set — the AY port
    public void Port1ffdIgnoresOtherAddresses(ushort port)
    {
        var (pager, _) = Build();
        pager.Out(port, 0x01);
        Assert.False(pager.SpecialMode);
    }

    [Fact]
    public void WritingPort1ffdDoesNotDisturbThe7ffdLatch()
    {
        // 0x1FFD has A15 and A1 low, so under the 128's decode it would also hit
        // the 0x7FFD latch and corrupt the bank, ROM and screen bits. The +2A/+3
        // narrows 0x7FFD to require A14 set, which 0x1FFD does not have.
        var (pager, _) = Build();

        pager.Out(Port7ffd, 0x10 | 0x08 | 0x03);   // ROM low bit, shadow screen, bank 3
        pager.Out(Port1ffd, 0x01);                  // special mode

        Assert.True(pager.SpecialMode);
        Assert.Equal(3, pager.PagedBank);
        Assert.Equal(7, pager.ScreenBank);
        Assert.Equal(1, pager.RomIndex);            // low bit survived
    }

    [Fact]
    public void Port7ffdRequiresA14Set()
    {
        // 0x3FFD has A15 and A1 low but A14 low, so the +2A/+3 ignores it here.
        var (pager, _) = Build();

        pager.Out(0x3FFD, 0x05);

        Assert.Equal(0, pager.PagedBank);
    }

    // ── Contention: banks 4-7, not the odd ones ──────────────────────────────

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)]
    [InlineData(4, true)]  [InlineData(5, true)]  [InlineData(6, true)]  [InlineData(7, true)]
    public void PagedWindowIsContendedForBanks4To7(int bank, bool contended)
    {
        var (pager, _) = Build();
        pager.Out(Port7ffd, (byte)bank);
        Assert.Equal(contended, pager.IsContended(0xC000));
    }

    [Fact]
    public void NormalMode_FixedWindowContention()
    {
        var (pager, _) = Build();

        Assert.False(pager.IsContended(0x0000));  // ROM
        Assert.True(pager.IsContended(0x4000));   // bank 5
        Assert.False(pager.IsContended(0x8000));  // bank 2 — below 4, so uncontended
    }

    [Theory]
    // In special mode every window's contention depends on the bank mapped there.
    [InlineData(0x01, false, false, false, false)]  // banks 0,1,2,3
    [InlineData(0x03, true,  true,  true,  true)]   // banks 4,5,6,7
    [InlineData(0x05, true,  true,  true,  false)]  // banks 4,5,6,3
    [InlineData(0x07, true,  true,  true,  false)]  // banks 4,7,6,3
    public void SpecialModeContentionFollowsTheMappedBanks(byte v1ffd, bool c0, bool c1, bool c2, bool c3)
    {
        var (pager, _) = Build();
        pager.Out(Port1ffd, v1ffd);

        Assert.Equal(c0, pager.IsContended(0x0000));
        Assert.Equal(c1, pager.IsContended(0x4000));
        Assert.Equal(c2, pager.IsContended(0x8000));
        Assert.Equal(c3, pager.IsContended(0xC000));
    }

    [Fact]
    public void In_ReturnsOpenBus_BothPortsAreWriteOnly()
    {
        var (pager, _) = Build();
        Assert.Equal(0xFF, pager.In(Port7ffd));
        Assert.Equal(0xFF, pager.In(Port1ffd));
    }

    [Fact]
    public void Reset_ReturnsToNormalModeAndClearsTheLock()
    {
        var (pager, bus) = Build();

        pager.Out(Port1ffd, 0x07);
        pager.Out(Port7ffd, 0x20);
        Assert.True(pager.SpecialMode);
        Assert.True(pager.PagingLocked);

        pager.Reset();

        Assert.False(pager.SpecialMode);
        Assert.False(pager.PagingLocked);
        Assert.Equal(0, pager.RomIndex);
        Assert.Equal(0xA0, bus.Read(0x0000));
    }
}
