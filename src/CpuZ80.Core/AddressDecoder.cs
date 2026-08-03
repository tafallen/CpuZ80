namespace CpuZ80.Core;

/// <summary>
/// Routes CPU bus traffic to hardware components by address range.
/// Multiple ranges may be registered; the conflict policy determines behavior on overlap.
/// Unmapped reads return 0xFF (open bus); unmapped writes are silent.
/// Supports byte-level granularity for address mappings.
/// </summary>
/// <remarks>
/// Two-level routing. The common case is a 256-entry page table covering
/// 256-byte pages; a page whose mapping is not uniform falls back to a
/// per-address table that is only allocated when something actually needs byte
/// granularity. Every machine in this repo maps on page boundaries, so the
/// fine-grained table is normally never allocated at all — routing metadata is
/// 4 KB rather than the 1 MiB a flat 65,536-entry table costs.
///
/// The page table also makes bank switching cheap: replacing a 16K window is 64
/// entries instead of 16,384, which matters for machines that page memory at
/// runtime (Spectrum 128K, Amstrad CPC, MSX). Use <see cref="Remap"/> for that —
/// it replaces rather than merging under the conflict policy.
/// </remarks>
public sealed class AddressDecoder : IBus
{
    public enum ConflictPolicy
    {
        LastRegistrationWins,
        LogicalAnd
    }

    private readonly struct Mapping
    {
        public readonly IBus? Device;
        public readonly ushort BaseAddress;
        public readonly ushort AddressMask;
        public readonly bool IsMirrored;

        public Mapping(IBus? device, ushort baseAddress, ushort addressMask = 0, bool isMirrored = false)
        {
            Device = device;
            BaseAddress = baseAddress;
            AddressMask = addressMask;
            IsMirrored = isMirrored;
        }
    }

    private const int PageShift = 8;
    private const int PageSize = 1 << PageShift;   // 256 bytes
    private const int PageCount = 65536 / PageSize; // 256 pages

    /// <summary>Uniform mapping for each 256-byte page, or <see cref="_fineRouter"/> if the page is split.</summary>
    private readonly Mapping[] _pages = new Mapping[PageCount];

    /// <summary>Per-address mappings. Allocated only when byte granularity is first needed.</summary>
    private Mapping[]? _fine;

    /// <summary>
    /// Stands in as the device for a page whose mapping varies within the page.
    /// Routing through a device — the same trick <see cref="LogicalAndBus"/>
    /// uses — keeps the read path free of any split test: it is one page-table
    /// load, one null check and one dispatch, exactly as a uniform page.
    /// </summary>
    private FinePageRouter? _fineRouter;

    private bool IsSplit(int page) => _fineRouter is not null && ReferenceEquals(_pages[page].Device, _fineRouter);

    private readonly ConflictPolicy _policy;

    public AddressDecoder(ConflictPolicy policy = ConflictPolicy.LastRegistrationWins)
    {
        _policy = policy;
    }

    /// <summary>Register <paramref name="device"/> for addresses [<paramref name="from"/>..<paramref name="to"/>] inclusive.</summary>
    public void Map(ushort from, ushort to, IBus device)
    {
        if (from > to)
        {
            throw new ArgumentException("Start address ('from') must be less than or equal to end address ('to').");
        }

        ApplyRange(from, to, new Mapping(device, from), replace: false);
    }

    /// <summary>
    /// Replaces whatever is mapped over [<paramref name="from"/>..<paramref name="to"/>]
    /// with <paramref name="device"/>, ignoring the conflict policy. Pass null to
    /// unmap the range (reads return open bus).
    /// </summary>
    /// <remarks>
    /// This is the bank-switching entry point. Unlike <see cref="Map"/> it does
    /// not merge with existing mappings, so paging the same window repeatedly
    /// cannot accumulate devices. Costs one table write per 256-byte page when
    /// the range is page-aligned.
    /// </remarks>
    public void Remap(ushort from, ushort to, IBus? device)
    {
        if (from > to)
        {
            throw new ArgumentException("Start address ('from') must be less than or equal to end address ('to').");
        }

        ApplyRange(from, to, new Mapping(device, from), replace: true);
    }

    /// <summary>
    /// Registers a device with bitmask-based mirroring.
    /// The device responds if (address &amp; decodeMask) == baseAddress.
    /// The device receives (address &amp; addressMask) as the internal offset.
    /// </summary>
    public void MapMirror(ushort baseAddress, ushort decodeMask, ushort addressMask, IBus device)
    {
        // TD-027: Validate that addressMask + 1 is a power of two and fits the device.
        int capacity = (addressMask + 1);
        if ((capacity & addressMask) != 0)
        {
            throw new ArgumentException($"Invalid address mask 0x{addressMask:X4}. Mask + 1 must be a power of two.");
        }

        // Check against known device sizes if available
        if (device is Ram ram && capacity > ram.Size)
        {
            throw new ArgumentException($"Address mask 0x{addressMask:X4} exceeds RAM capacity (0x{ram.Size:X4}).");
        }
        if (device is Rom rom && capacity > rom.Size)
        {
            throw new ArgumentException($"Address mask 0x{addressMask:X4} exceeds ROM capacity (0x{rom.Size:X4}).");
        }

        var mapping = new Mapping(device, baseAddress, addressMask, true);

        // When the decode mask ignores the low byte, membership is decided by the
        // page number alone and whole pages can be set directly.
        if ((decodeMask & 0xFF) == 0)
        {
            for (int page = 0; page < PageCount; page++)
            {
                if (((page << PageShift) & decodeMask) == baseAddress)
                {
                    SetWholePage(page, mapping, replace: false);
                }
            }
            return;
        }

        for (int i = 0; i < 65536; i++)
        {
            if ((i & decodeMask) == baseAddress)
            {
                SetSingleAddress((ushort)i, mapping, replace: false);
            }
        }
    }

    /// <summary>Applies <paramref name="mapping"/> across a range, whole pages at a time where possible.</summary>
    private void ApplyRange(ushort from, ushort to, Mapping mapping, bool replace)
    {
        int firstPage = from >> PageShift;
        int lastPage = to >> PageShift;

        for (int page = firstPage; page <= lastPage; page++)
        {
            int pageStart = page << PageShift;
            int pageEnd = pageStart + PageSize - 1;

            if (from <= pageStart && to >= pageEnd)
            {
                SetWholePage(page, mapping, replace);
            }
            else
            {
                int start = Math.Max(from, pageStart);
                int end = Math.Min(to, pageEnd);
                for (int addr = start; addr <= end; addr++)
                {
                    SetSingleAddress((ushort)addr, mapping, replace);
                }
            }
        }
    }

    private void SetWholePage(int page, Mapping mapping, bool replace)
    {
        // Replacing, or landing on an empty page under LastRegistrationWins:
        // the page becomes uniform again and any split state is discarded.
        if (replace || (!IsSplit(page) && (_policy == ConflictPolicy.LastRegistrationWins || _pages[page].Device is null)))
        {
            _pages[page] = mapping;
            return;
        }

        // Merging under LogicalAnd against an existing mapping. If the page is
        // already uniform the merge is too, so it can stay a single entry.
        if (!IsSplit(page))
        {
            _pages[page] = Merge(_pages[page], mapping);
            return;
        }

        int pageStart = page << PageShift;
        for (int addr = pageStart; addr < pageStart + PageSize; addr++)
        {
            SetSingleAddress((ushort)addr, mapping, replace: false);
        }
    }

    private void SetSingleAddress(ushort address, Mapping mapping, bool replace)
    {
        int page = address >> PageShift;
        SplitPage(page);

        _fine![address] = replace ? mapping : Merge(_fine[address], mapping);
    }

    /// <summary>Promotes a uniform page to per-address entries so it can hold a mixed mapping.</summary>
    private void SplitPage(int page)
    {
        if (IsSplit(page)) return;

        if (_fine is null)
        {
            _fine = new Mapping[65536];
            _fineRouter = new FinePageRouter(this);
        }

        int pageStart = page << PageShift;
        Mapping uniform = _pages[page];
        for (int addr = pageStart; addr < pageStart + PageSize; addr++)
        {
            _fine[addr] = uniform;
        }

        // BaseAddress 0 and not mirrored, so the router receives the absolute address.
        _pages[page] = new Mapping(_fineRouter, 0);
    }

    /// <summary>Combines an incoming mapping with what is already there, per the conflict policy.</summary>
    private Mapping Merge(Mapping existing, Mapping incoming)
    {
        if (existing.Device is null || _policy == ConflictPolicy.LastRegistrationWins)
        {
            return incoming;
        }

        // LogicalAnd: model devices pulling the same data lines.
        if (existing.Device is LogicalAndBus andBus)
        {
            andBus.Add(incoming.Device!, incoming.BaseAddress, incoming.AddressMask, incoming.IsMirrored);
            return existing;
        }

        var conflict = new LogicalAndBus();
        conflict.Add(existing.Device, existing.BaseAddress, existing.AddressMask, existing.IsMirrored);
        conflict.Add(incoming.Device!, incoming.BaseAddress, incoming.AddressMask, incoming.IsMirrored);
        return new Mapping(conflict, 0); // BaseAddress is ignored by the conflict bus
    }

    public byte Read(ushort address)
    {
        Mapping mapping = _pages[address >> PageShift];
        if (mapping.Device is null) return 0xFF;

        ushort offset = mapping.IsMirrored
            ? (ushort)(address & mapping.AddressMask)
            : (ushort)(address - mapping.BaseAddress);

        return mapping.Device.Read(offset);
    }

    public void Write(ushort address, byte value)
    {
        Mapping mapping = _pages[address >> PageShift];
        if (mapping.Device is null) return;

        ushort offset = mapping.IsMirrored
            ? (ushort)(address & mapping.AddressMask)
            : (ushort)(address - mapping.BaseAddress);

        mapping.Device.Write(offset, value);
    }

    /// <summary>Routes within a page whose mapping is not uniform.</summary>
    private sealed class FinePageRouter : IBus
    {
        private readonly AddressDecoder _owner;

        public FinePageRouter(AddressDecoder owner) => _owner = owner;

        public byte Read(ushort address)
        {
            Mapping m = _owner._fine![address];
            if (m.Device is null) return 0xFF;
            ushort offset = m.IsMirrored
                ? (ushort)(address & m.AddressMask)
                : (ushort)(address - m.BaseAddress);
            return m.Device.Read(offset);
        }

        public void Write(ushort address, byte value)
        {
            Mapping m = _owner._fine![address];
            if (m.Device is null) return;
            ushort offset = m.IsMirrored
                ? (ushort)(address & m.AddressMask)
                : (ushort)(address - m.BaseAddress);
            m.Device.Write(offset, value);
        }
    }

    /// <summary>
    /// Models physical bus contention where multiple devices pull the same data lines.
    /// </summary>
    private sealed class LogicalAndBus : IBus
    {
        private readonly List<(IBus Device, ushort Base, ushort Mask, bool Mirror)> _devices = new();

        public void Add(IBus device, ushort baseAddr, ushort mask, bool mirror)
        {
            _devices.Add((device, baseAddr, mask, mirror));
        }

        public byte Read(ushort address)
        {
            // Address passed here is the absolute bus address (Mapping base was 0)
            byte result = 0xFF;
            foreach (var d in _devices)
            {
                ushort offset = d.Mirror ? (ushort)(address & d.Mask) : (ushort)(address - d.Base);
                result &= d.Device.Read(offset);
            }
            return result;
        }

        public void Write(ushort address, byte value)
        {
            foreach (var d in _devices)
            {
                ushort offset = d.Mirror ? (ushort)(address & d.Mask) : (ushort)(address - d.Base);
                d.Device.Write(offset, value);
            }
        }
    }
}
