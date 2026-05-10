namespace CpuZ80.Core;

/// <summary>
/// Routes I/O port traffic to hardware components by address range or bitmask.
/// Supports hardware-accurate conflict resolution (Logical AND).
/// </summary>
public sealed class PortDecoder : IPortBus
{
    public enum ConflictPolicy
    {
        LastRegistrationWins,
        LogicalAnd
    }

    private readonly struct Mapping
    {
        public readonly IPortBus? Device;
        public readonly ushort   BaseAddress;
        public readonly ushort   AddressMask;
        public readonly bool     IsMirrored;

        public Mapping(IPortBus? device, ushort baseAddress, ushort addressMask = 0, bool isMirrored = false)
        {
            Device      = device;
            BaseAddress = baseAddress;
            AddressMask = addressMask;
            IsMirrored  = isMirrored;
        }
    }

    private readonly Mapping[] _map = new Mapping[65536];
    private readonly ConflictPolicy _policy;

    public PortDecoder(ConflictPolicy policy = ConflictPolicy.LogicalAnd)
    {
        _policy = policy;
    }

    /// <summary>Value returned when no device responds to an 'In' request.</summary>
    public byte OpenBusValue { get; set; } = 0xFF;

    /// <summary>Registers a device for a contiguous range of ports.</summary>
    public void Map(ushort from, ushort to, IPortBus device)
    {
        if (from > to) throw new ArgumentException("'from' must be <= 'to'");

        for (int i = from; i <= to; i++)
        {
            UpdateMapping((ushort)i, new Mapping(device, from));
            if (i == 65535) break;
        }
    }

    /// <summary>
    /// Registers a device with bitmask-based decoding (mirrored across the 16-bit space).
    /// </summary>
    public void MapMirror(ushort baseAddress, ushort decodeMask, ushort addressMask, IPortBus device)
    {
        for (int i = 0; i < 65536; i++)
        {
            if ((i & decodeMask) == baseAddress)
            {
                UpdateMapping((ushort)i, new Mapping(device, baseAddress, addressMask, true));
            }
        }
    }

    private void UpdateMapping(ushort port, Mapping newMapping)
    {
        var existing = _map[port];
        if (existing.Device == null || _policy == ConflictPolicy.LastRegistrationWins)
        {
            _map[port] = newMapping;
        }
        else if (_policy == ConflictPolicy.LogicalAnd)
        {
            if (existing.Device is LogicalAndPortBus andBus)
            {
                andBus.Add(newMapping.Device!, newMapping.BaseAddress, newMapping.AddressMask, newMapping.IsMirrored);
            }
            else
            {
                var conflict = new LogicalAndPortBus();
                conflict.Add(existing.Device!, existing.BaseAddress, existing.AddressMask, existing.IsMirrored);
                conflict.Add(newMapping.Device!, newMapping.BaseAddress, newMapping.AddressMask, newMapping.IsMirrored);
                _map[port] = new Mapping(conflict, 0); 
            }
        }
    }

    public byte In(ushort port)
    {
        var mapping = _map[port];
        if (mapping.Device == null) return OpenBusValue;

        ushort offset = mapping.IsMirrored 
            ? (ushort)(port & mapping.AddressMask)
            : (ushort)(port - mapping.BaseAddress);

        return mapping.Device.In(offset);
    }

    public void Out(ushort port, byte value)
    {
        var mapping = _map[port];
        if (mapping.Device == null) return;

        ushort offset = mapping.IsMirrored 
            ? (ushort)(port & mapping.AddressMask)
            : (ushort)(port - mapping.BaseAddress);

        mapping.Device.Out(offset, value);
    }

    private sealed class LogicalAndPortBus : IPortBus
    {
        private readonly List<(IPortBus Device, ushort Base, ushort Mask, bool Mirror)> _devices = new();

        public void Add(IPortBus device, ushort baseAddr, ushort mask, bool mirror)
        {
            _devices.Add((device, baseAddr, mask, mirror));
        }

        public byte In(ushort port)
        {
            byte result = 0xFF;
            foreach (var d in _devices)
            {
                ushort offset = d.Mirror ? (ushort)(port & d.Mask) : (ushort)(port - d.Base);
                result &= d.Device.In(offset);
            }
            return result;
        }

        public void Out(ushort port, byte value)
        {
            foreach (var d in _devices)
            {
                ushort offset = d.Mirror ? (ushort)(port & d.Mask) : (ushort)(port - d.Base);
                d.Device.Out(offset, value);
            }
        }
    }
}
