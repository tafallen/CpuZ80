using CpuZ80.Core;

namespace Machines.ZxSpectrum128;

/// <summary>
/// The ZX Spectrum 128's memory paging latch on port 0x7FFD.
/// </summary>
/// <remarks>
/// The 128 has eight 16K RAM banks and two 16K ROMs in a 64K address space:
///
/// <code>
/// 0x0000-0x3FFF   ROM 0 (128 editor) or ROM 1 (48 BASIC)   — bit 4
/// 0x4000-0x7FFF   always bank 5                            — always contended
/// 0x8000-0xBFFF   always bank 2                            — never contended
/// 0xC000-0xFFFF   any bank 0-7                             — bits 0-2
/// </code>
///
/// Banks 1, 3, 5 and 7 are contended, so whether 0xC000-0xFFFF stalls the CPU
/// depends on runtime paging state rather than the address alone — see
/// <see cref="IsContended"/>.
///
/// The port is write-only and decoded on A15 = 0 and A1 = 0 only, so it responds
/// to any address matching <c>0xxxxxxx xxxxxx0x</c>, not just 0x7FFD. Software
/// does rely on this.
///
/// Bit 5 is a one-way latch: once set, every later write is ignored until
/// <see cref="Reset"/>. The 48 BASIC ROM sets it, which is how the machine drops
/// into "48K mode".
///
/// See docs/zx-spectrum-128.md.
/// </remarks>
public sealed class Zx128MemoryPager : IPortBus
{
    /// <summary>The port answers when A15 and A1 are both low.</summary>
    private const ushort DecodeMask = 0x8002;

    private const ushort RomWindowStart  = 0x0000;
    private const ushort RomWindowEnd    = 0x3FFF;
    private const ushort PagedWindowStart = 0xC000;
    private const ushort PagedWindowEnd   = 0xFFFF;

    private const int NormalScreenBank = 5;
    private const int ShadowScreenBank = 7;

    private readonly AddressDecoder _bus;
    private readonly Ram[] _banks;
    private readonly Rom[] _roms;

    /// <summary>RAM bank currently mapped at 0xC000 (0-7).</summary>
    public int PagedBank { get; private set; }

    /// <summary>ROM currently mapped at 0x0000 (0 = 128 editor, 1 = 48 BASIC).</summary>
    public int RomIndex { get; private set; }

    /// <summary>Bank the ULA displays: 5 normally, 7 for the shadow screen.</summary>
    public int ScreenBank { get; private set; }

    /// <summary>True once bit 5 has been written; no further paging until reset.</summary>
    public bool PagingLocked { get; private set; }

    /// <summary>Raised after any change to the paging state, so the machine can re-point the display.</summary>
    public event Action? PagingChanged;

    public Zx128MemoryPager(AddressDecoder bus, Ram[] banks, Rom[] roms)
    {
        if (banks.Length != 8) throw new ArgumentException($"Expected 8 RAM banks, got {banks.Length}.", nameof(banks));
        if (roms.Length != 2) throw new ArgumentException($"Expected 2 ROMs, got {roms.Length}.", nameof(roms));

        _bus = bus;
        _banks = banks;
        _roms = roms;
    }

    /// <summary>Restores the power-on configuration: ROM 0, bank 0 paged, normal screen, paging enabled.</summary>
    public void Reset()
    {
        PagingLocked = false;
        RomIndex = 0;
        PagedBank = 0;
        ScreenBank = NormalScreenBank;

        // The fixed windows never move, so they are mapped once here.
        _bus.Remap(0x4000, 0x7FFF, _banks[5]);
        _bus.Remap(0x8000, 0xBFFF, _banks[2]);

        ApplyPaging();
    }

    public byte In(ushort port)
    {
        // Write-only: reads see the floating bus, which the port decoder supplies
        // as open bus for anything unclaimed.
        return 0xFF;
    }

    public void Out(ushort port, byte value)
    {
        if ((port & DecodeMask) != 0) return;
        if (PagingLocked) return;

        PagedBank  = value & 0x07;
        ScreenBank = (value & 0x08) != 0 ? ShadowScreenBank : NormalScreenBank;
        RomIndex   = (value & 0x10) != 0 ? 1 : 0;

        // Latched last: this write still takes effect, subsequent ones do not.
        if ((value & 0x20) != 0) PagingLocked = true;

        ApplyPaging();
    }

    /// <summary>
    /// True if an access to <paramref name="address"/> is subject to ULA
    /// contention, given what is currently paged in.
    /// </summary>
    public bool IsContended(ushort address)
    {
        // 0x4000-0x7FFF is bank 5, which is contended.
        if (address >= 0x4000 && address <= 0x7FFF) return true;

        // 0xC000-0xFFFF is contended only when an odd bank is paged there.
        if (address >= 0xC000) return (PagedBank & 1) != 0;

        // ROM and bank 2 are never contended.
        return false;
    }

    /// <summary>Points the two movable windows at the currently selected ROM and bank.</summary>
    private void ApplyPaging()
    {
        _bus.Remap(RomWindowStart, RomWindowEnd, _roms[RomIndex]);
        _bus.Remap(PagedWindowStart, PagedWindowEnd, _banks[PagedBank]);
        PagingChanged?.Invoke();
    }
}
