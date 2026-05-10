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

        public Mapping(IBus? device, ushort baseAddress)
        {
            Device = device;
            BaseAddress = baseAddress;
        }
    }

    // 64 KB lookup table for byte-level granularity.
    // Memory overhead is ~512 KB (65536 * 8 bytes), which is negligible on modern systems
    // and provides the fastest possible O(1) routing.
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
            if (i == 65535) break; // Avoid overflow in ushort loop if 'to' is FFFF
        }
    }

    public byte Read(ushort address)
    {
        var mapping = _map[address];
        return mapping.Device is not null ? mapping.Device.Read((ushort)(address - mapping.BaseAddress)) : (byte)0xFF;
    }

    public void Write(ushort address, byte value)
    {
        var mapping = _map[address];
        mapping.Device?.Write((ushort)(address - mapping.BaseAddress), value);
    }
}
