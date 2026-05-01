namespace CpuZ80.Core;

/// <summary>
/// Routes CPU bus traffic to hardware components by address range.
/// Multiple ranges may be registered; the last registration wins on overlap.
/// Unmapped reads return 0xFF (open bus); unmapped writes are silent.
/// </summary>
public sealed class AddressDecoder : IBus
{
    private readonly struct PageEntry
    {
        public readonly IBus? Device;
        public readonly ushort BaseAddress;

        public PageEntry(IBus? device, ushort baseAddress)
        {
            Device = device;
            BaseAddress = baseAddress;
        }
    }

    private readonly PageEntry[] _pages = new PageEntry[256];
    private readonly record struct Mapping(ushort From, ushort To, IBus Device);
    private readonly List<Mapping> _mappings = new();

    /// <summary>Register <paramref name="device"/> for addresses [<paramref name="from"/>..<paramref name="to"/>] inclusive.</summary>
    public void Map(ushort from, ushort to, IBus device)
        => _mappings.Add(new Mapping(from, to, device));

    public byte Read(ushort address)
    {
        var (device, from) = Resolve(address);
        return device is not null ? device.Read((ushort)(address - from)) : (byte)0xFF;
    }

    public void Write(ushort address, byte value)
    {
        var (device, from) = Resolve(address);
        device?.Write((ushort)(address - from), value);
    }

    // Iterate in reverse so the last-registered mapping wins on overlap.
    private (IBus? Device, ushort From) Resolve(ushort address)
    {
        for (int i = _mappings.Count - 1; i >= 0; i--)
        {
            var m = _mappings[i];
            if (address >= m.From && address <= m.To)
                return (m.Device, m.From);
        }
        return (null, 0);
    }
}
