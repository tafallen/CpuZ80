namespace CpuZ80.Core;

/// <summary>
/// Routes CPU bus traffic to hardware components by address range.
/// Multiple ranges may be registered; the last registration wins on overlap.
/// Unmapped reads return 0xFF (open bus); unmapped writes are silent.
/// Supports byte-level granularity for address mappings.
/// </summary>
public sealed class AddressDecoder : IBus
{
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

    // 64 KB lookup table for byte-level granularity.
    private readonly Mapping[] _map = new Mapping[65536];

    /// <summary>Register <paramref name="device"/> for addresses [<paramref name="from"/>..<paramref name="to"/>] inclusive.</summary>
    public void Map(ushort from, ushort to, IBus device)
    {
        if (from > to)
        {
            throw new ArgumentException("Start address ('from') must be less than or equal to end address ('to').");
        }

        for (int i = from; i <= to; i++)
        {
            _map[i] = new Mapping(device, from);
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
        for (int i = 0; i < 65536; i++)
        {
            if ((i & decodeMask) == baseAddress)
            {
                _map[i] = new Mapping(device, baseAddress, addressMask, true);
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
}
