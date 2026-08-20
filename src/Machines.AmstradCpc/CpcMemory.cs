using CpuZ80.Core;

namespace Machines.AmstradCpc;

/// <summary>
/// The CPC memory map: 64K or 128K of RAM, a lower ROM, and a bank of upper
/// ROMs, arranged in four 16K windows.
/// </summary>
/// <remarks>
/// Two things differ from every Sinclair machine in this repo:
///
/// <b>ROM is an overlay, not a replacement.</b> With a ROM paged in, reads come
/// from the ROM but writes still reach the RAM underneath. The firmware relies
/// on this — it keeps variables in the RAM beneath the lower ROM.
///
/// <b>The eight RAM configurations are a lookup table.</b> Config 3 puts base
/// bank 3 at <c>&amp;4000</c> rather than bank 1, which no formula covering the
/// other seven reproduces.
///
/// See docs/amstrad-cpc.md §2.3.
/// </remarks>
public sealed class CpcMemory
{
    public const int BankSize = 0x4000;
    private const int WindowCount = 4;

    /// <summary>
    /// RAM banks in 16K units. Banks 0-3 are the base 64K; 4-7 are the second
    /// 64K a 6128 adds.
    /// </summary>
    public Ram[] Banks { get; }

    /// <summary>True when the machine has the second 64K, i.e. it is a 6128.</summary>
    public bool Has128K => Banks.Length > 4;

    /// <summary>The lower ROM: the operating system, at &amp;0000 when enabled.</summary>
    public Rom LowerRom { get; }

    /// <summary>Upper ROMs by number. 0 is BASIC; 7 is AMSDOS on a machine with a drive.</summary>
    private readonly Dictionary<int, Rom> _upperRoms = [];

    private readonly AddressDecoder _bus;
    private readonly RomOverlay?[] _lowerOverlay = new RomOverlay?[1];
    private readonly Dictionary<int, RomOverlay> _upperOverlays = [];

    /// <summary>
    /// The eight RAM configurations, as bank numbers per 16K window. Config 3's
    /// second entry is 3, not 1 — see the class remarks.
    /// </summary>
    private static readonly int[][] RamConfigs =
    [
        [0, 1, 2, 3],
        [0, 1, 2, 7],
        [4, 5, 6, 7],
        [0, 3, 2, 7],
        [0, 4, 2, 3],
        [0, 5, 2, 3],
        [0, 6, 2, 3],
        [0, 7, 2, 3],
    ];

    /// <summary>Lower ROM enabled. Reset enables it, because the CPU starts executing at &amp;0000.</summary>
    public bool LowerRomEnabled { get; private set; } = true;

    /// <summary>Upper ROM enabled.</summary>
    public bool UpperRomEnabled { get; private set; } = true;

    /// <summary>Which upper ROM is selected, from the ROM select port.</summary>
    public int UpperRomNumber { get; private set; }

    /// <summary>The active RAM configuration, 0-7.</summary>
    public int RamConfig { get; private set; }

    public CpcMemory(AddressDecoder bus, byte[] lowerRom, byte[] upperRom, bool has128K = true)
    {
        _bus = bus;

        LowerRom = new Rom(Require16K(lowerRom, nameof(lowerRom)));

        int bankCount = has128K ? 8 : 4;
        Banks = new Ram[bankCount];
        for (int i = 0; i < bankCount; i++) Banks[i] = new Ram(BankSize);

        AddUpperRom(0, upperRom);
        ApplyMapping();
    }

    /// <summary>
    /// Fits an upper ROM at <paramref name="number"/>. ROM 7 is AMSDOS on a
    /// machine with a disk drive.
    /// </summary>
    public void AddUpperRom(int number, byte[] image)
    {
        if (number is < 0 or > 251)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Upper ROM numbers run 0-251.");
        }

        _upperRoms[number] = new Rom(Require16K(image, nameof(image)));
        _upperOverlays.Remove(number);
        ApplyMapping();
    }

    public bool HasUpperRom(int number) => _upperRoms.ContainsKey(number);

    private static byte[] Require16K(byte[] image, string paramName)
    {
        byte[] stripped = AmsdosHeader.Strip(image);

        if (stripped.Length != BankSize)
        {
            throw new ArgumentException(
                $"A CPC ROM is {BankSize} bytes, got {stripped.Length}" +
                (stripped.Length == image.Length ? "" : $" after stripping an AMSDOS header from {image.Length}") +
                ".", paramName);
        }

        return stripped;
    }

    public void Reset()
    {
        LowerRomEnabled = true;
        UpperRomEnabled = true;
        UpperRomNumber = 0;
        RamConfig = 0;
        ApplyMapping();
    }

    /// <summary>Applies the Gate Array's RMR ROM enables. Both are active low at the port.</summary>
    public void SetRomEnables(bool lowerEnabled, bool upperEnabled)
    {
        if (lowerEnabled == LowerRomEnabled && upperEnabled == UpperRomEnabled) return;

        LowerRomEnabled = lowerEnabled;
        UpperRomEnabled = upperEnabled;
        ApplyMapping();
    }

    public void SelectUpperRom(int number)
    {
        if (number == UpperRomNumber) return;

        UpperRomNumber = number;
        ApplyMapping();
    }

    public void SetRamConfig(int config)
    {
        config &= 0x07;
        if (config == RamConfig) return;

        RamConfig = config;
        ApplyMapping();
    }

    /// <summary>The RAM bank currently mapped into <paramref name="window"/> (0-3).</summary>
    public int BankAt(int window)
    {
        // Banking is decoded by the expansion PAL, not the Gate Array. A 64K
        // machine has no PAL, so the register does nothing at all and the map
        // stays 0,1,2,3 however it is written.
        //
        // Masking the bank number instead would be wrong in a way that looks
        // reasonable: config 3 would put base bank 3 at 0x4000 on a machine
        // that cannot bank at all.
        if (!Has128K) return window;

        return RamConfigs[RamConfig][window];
    }

    private void ApplyMapping()
    {
        for (int window = 0; window < WindowCount; window++)
        {
            ushort from = (ushort)(window * BankSize);
            ushort to = (ushort)(from + BankSize - 1);
            var ram = Banks[BankAt(window)];

            IBus device = ram;

            if (window == 0 && LowerRomEnabled)
            {
                device = LowerOverlay(ram);
            }
            else if (window == 3 && UpperRomEnabled && _upperRoms.TryGetValue(UpperRomNumber, out var upper))
            {
                device = UpperOverlay(upper, ram);
            }

            _bus.Remap(from, to, device);
        }
    }

    private RomOverlay LowerOverlay(Ram ram)
    {
        var overlay = _lowerOverlay[0];
        if (overlay is null || !overlay.Matches(LowerRom, ram))
        {
            overlay = new RomOverlay(LowerRom, ram);
            _lowerOverlay[0] = overlay;
        }
        return overlay;
    }

    private RomOverlay UpperOverlay(Rom rom, Ram ram)
    {
        if (_upperOverlays.TryGetValue(UpperRomNumber, out var overlay) && overlay.Matches(rom, ram))
        {
            return overlay;
        }

        overlay = new RomOverlay(rom, ram);
        _upperOverlays[UpperRomNumber] = overlay;
        return overlay;
    }
}
