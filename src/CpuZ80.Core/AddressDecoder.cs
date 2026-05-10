namespace CpuZ80.Core;

/// <summary>
/// Routes CPU bus traffic to hardware components by address range.
/// Multiple ranges may be registered; the conflict policy determines behavior on overlap.
/// Unmapped reads return 0xFF (open bus); unmapped writes are silent.
/// Supports byte-level granularity for address mappings.
/// </summary>
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

    private readonly Mapping[] _map = new Mapping[65536];
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

        for (int i = from; i <= to; i++)
        {
            UpdateMapping((ushort)i, new Mapping(device, from));
            if (i == 65535) break; 
        }
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

        for (int i = 0; i < 65536; i++)
        {
            if ((i & decodeMask) == baseAddress)
            {
                UpdateMapping((ushort)i, new Mapping(device, baseAddress, addressMask, true));
            }
        }
    }

    private void UpdateMapping(ushort address, Mapping newMapping)
    {
        var existing = _map[address];
        if (existing.Device == null || _policy == ConflictPolicy.LastRegistrationWins)
        {
            _map[address] = newMapping;
        }
        else if (_policy == ConflictPolicy.LogicalAnd)
        {
            // If already a conflict bus, add to it. Otherwise create a new one.
            if (existing.Device is LogicalAndBus andBus)
            {
                andBus.Add(newMapping.Device!, newMapping.BaseAddress, newMapping.AddressMask, newMapping.IsMirrored);
            }
            else
            {
                var conflict = new LogicalAndBus();
                conflict.Add(existing.Device!, existing.BaseAddress, existing.AddressMask, existing.IsMirrored);
                conflict.Add(newMapping.Device!, newMapping.BaseAddress, newMapping.AddressMask, newMapping.IsMirrored);
                _map[address] = new Mapping(conflict, 0); // BaseAddress is ignored by ConflictBus
            }
        }
    }

    public byte Read(ushort address)
    {
        var mapping = _map[address];
        if (mapping.Device is null) return 0xFF;
        
        ushort offset = mapping.IsMirrored 
            ? (ushort)(address & mapping.AddressMask)
            : (ushort)(address - mapping.BaseAddress);
            
        return mapping.Device.Read(offset);
    }

    public void Write(ushort address, byte value)
    {
        var mapping = _map[address];
        if (mapping.Device is null) return;

        ushort offset = mapping.IsMirrored 
            ? (ushort)(address & mapping.AddressMask)
            : (ushort)(address - mapping.BaseAddress);

        mapping.Device.Write(offset, value);
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
