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

    /// <summary>Register <paramref name="device"/> for addresses [<paramref name="from"/>..<paramref name="to"/>] inclusive.</summary>
    public void Map(ushort from, ushort to, IBus device)
    {
        int startPage = from >> 8;
        int endPage = to >> 8;

        for (int i = startPage; i <= endPage; i++)
        {
            _pages[i] = new PageEntry(device, from);
        }
    }

    public byte Read(ushort address)
    {
        var entry = _pages[address >> 8];
        return entry.Device is not null ? entry.Device.Read((ushort)(address - entry.BaseAddress)) : (byte)0xFF;
    }

    public void Write(ushort address, byte value)
    {
        var entry = _pages[address >> 8];
        entry.Device?.Write((ushort)(address - entry.BaseAddress), value);
    }
}
