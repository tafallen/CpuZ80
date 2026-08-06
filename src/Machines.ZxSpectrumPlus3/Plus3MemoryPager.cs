using CpuZ80.Core;

namespace Machines.ZxSpectrumPlus3;

/// <summary>
/// Memory paging for the ZX Spectrum +2A / +2B / +3 / +3B, across ports 0x7FFD
/// and 0x1FFD.
/// </summary>
/// <remarks>
/// A superset of the 128's scheme, differing in three ways that are easy to get
/// wrong by assuming the 128's behaviour:
///
/// <list type="number">
/// <item>There are <b>four</b> ROMs. The index is two bits: the low bit is
/// 0x7FFD bit 4, the high bit is 0x1FFD bit 2. ROM 0 is the editor, 1 the syntax
/// checker, 2 +3DOS and 3 48 BASIC.</item>
/// <item><b>Special (all-RAM) mode</b>, entered with 0x1FFD bit 0, maps RAM over
/// all four windows in one of four fixed configurations. It exists so CP/M can
/// run, which it cannot with ROM at 0x0000 — so in this mode the bottom 16K
/// becomes writable.</item>
/// <item>The contended banks are <b>4, 5, 6 and 7</b>, not the odd ones. In
/// special mode that means contention varies per window with the configuration,
/// not just at 0xC000.</item>
/// </list>
///
/// See docs/zx-spectrum-plus3.md.
/// </remarks>
public sealed class Plus3MemoryPager : IPortBus
{
    /// <summary>
    /// 0x7FFD answers when A1 and A15 are low <b>and A14 is high</b>.
    /// </summary>
    /// <remarks>
    /// Narrower than the 128, which decoded only A15 and A1. That mattered:
    /// 0x1FFD also has A15 and A1 low, so under the 128's rule a write to it
    /// would land in this latch too and corrupt the ROM and screen bits. A14 is
    /// what separates them — set for 0x7FFD, clear for 0x1FFD.
    /// </remarks>
    private const ushort Port7ffdMask  = 0xC002;
    private const ushort Port7ffdValue = 0x4000;

    /// <summary>0x1FFD answers when A12 is set and A13, A14, A15 and A1 are clear.</summary>
    private const ushort Port1ffdMask = 0xF002;
    private const ushort Port1ffdValue = 0x1000;

    private const int NormalScreenBank = 5;
    private const int ShadowScreenBank = 7;

    /// <summary>
    /// Bank layout for each special-mode configuration, indexed by 0x1FFD bits
    /// 2:1, in window order 0x0000 / 0x4000 / 0x8000 / 0xC000.
    /// </summary>
    private static readonly int[][] SpecialConfigs =
    [
        [0, 1, 2, 3],
        [4, 5, 6, 7],
        [4, 5, 6, 3],
        [4, 7, 6, 3],
    ];

    /// <summary>Banks 4-7 are contended on these machines.</summary>
    private static bool BankIsContended(int bank) => bank >= 4;

    private readonly AddressDecoder _bus;
    private readonly Ram[] _banks;
    private readonly Rom[] _roms;

    /// <summary>RAM bank at 0xC000 in normal mode (0-7).</summary>
    public int PagedBank { get; private set; }

    /// <summary>ROM at 0x0000 in normal mode (0-3), from both ports.</summary>
    public int RomIndex { get; private set; }

    /// <summary>Bank the ULA displays: 5 normally, 7 for the shadow screen.</summary>
    public int ScreenBank { get; private set; }

    /// <summary>True once 0x7FFD bit 5 has been written; no further paging until reset.</summary>
    public bool PagingLocked { get; private set; }

    /// <summary>True while 0x1FFD bit 0 selects the all-RAM configurations.</summary>
    public bool SpecialMode { get; private set; }

    /// <summary>Which all-RAM configuration is selected (0-3), from 0x1FFD bits 2:1.</summary>
    public int SpecialConfig { get; private set; }

    /// <summary>Raised after any paging change so the machine can re-point the display.</summary>
    public event Action? PagingChanged;

    public Plus3MemoryPager(AddressDecoder bus, Ram[] banks, Rom[] roms)
    {
        if (banks.Length != 8) throw new ArgumentException($"Expected 8 RAM banks, got {banks.Length}.", nameof(banks));
        if (roms.Length != 4) throw new ArgumentException($"Expected 4 ROMs, got {roms.Length}.", nameof(roms));

        _bus = bus;
        _banks = banks;
        _roms = roms;
    }

    /// <summary>Power-on configuration: normal mode, ROM 0, bank 0 paged, normal screen.</summary>
    public void Reset()
    {
        PagingLocked = false;
        SpecialMode = false;
        SpecialConfig = 0;
        RomIndex = 0;
        PagedBank = 0;
        ScreenBank = NormalScreenBank;

        ApplyPaging();
    }

    public byte In(ushort port) => 0xFF;   // both ports are write-only

    public void Out(ushort port, byte value)
    {
        if (PagingLocked) return;

        bool changed = false;

        if ((port & Port7ffdMask) == Port7ffdValue)
        {
            PagedBank  = value & 0x07;
            ScreenBank = (value & 0x08) != 0 ? ShadowScreenBank : NormalScreenBank;
            RomIndex   = (RomIndex & 0x02) | ((value & 0x10) != 0 ? 1 : 0);
            if ((value & 0x20) != 0) PagingLocked = true;
            changed = true;
        }

        if ((port & Port1ffdMask) == Port1ffdValue)
        {
            SpecialMode   = (value & 0x01) != 0;
            SpecialConfig = (value >> 1) & 0x03;
            RomIndex      = (RomIndex & 0x01) | ((value & 0x04) != 0 ? 2 : 0);
            changed = true;
        }

        if (changed) ApplyPaging();
    }

    /// <summary>
    /// True if an access to <paramref name="address"/> is subject to ULA
    /// contention, given what is currently paged in.
    /// </summary>
    public bool IsContended(ushort address)
    {
        int window = address >> 14;   // 0x0000 / 0x4000 / 0x8000 / 0xC000

        if (SpecialMode) return BankIsContended(SpecialConfigs[SpecialConfig][window]);

        return window switch
        {
            0 => false,                         // ROM
            1 => BankIsContended(5),            // always bank 5
            2 => BankIsContended(2),            // always bank 2 — below 4, uncontended
            _ => BankIsContended(PagedBank),
        };
    }

    /// <summary>Points the four windows at whatever the current mode selects.</summary>
    private void ApplyPaging()
    {
        if (SpecialMode)
        {
            int[] config = SpecialConfigs[SpecialConfig];
            _bus.Remap(0x0000, 0x3FFF, _banks[config[0]]);
            _bus.Remap(0x4000, 0x7FFF, _banks[config[1]]);
            _bus.Remap(0x8000, 0xBFFF, _banks[config[2]]);
            _bus.Remap(0xC000, 0xFFFF, _banks[config[3]]);
        }
        else
        {
            _bus.Remap(0x0000, 0x3FFF, _roms[RomIndex]);
            _bus.Remap(0x4000, 0x7FFF, _banks[5]);
            _bus.Remap(0x8000, 0xBFFF, _banks[2]);
            _bus.Remap(0xC000, 0xFFFF, _banks[PagedBank]);
        }

        PagingChanged?.Invoke();
    }
}
