using CpuZ80.Core;

namespace Machines.ZxSpectrum;

/// <summary>
/// Routes I/O traffic between the ULA and the Kempston interface using hardware-accurate decoding.
/// </summary>
internal sealed class ZxSpectrumPortBus : IPortBus
{
    // Semantic decoding masks (TD-034)
    private const ushort Ula_Decode_Mask      = 0x0001; // A0 must be low
    private const ushort Kempston_Decode_Mask = 0x0020; // A5 must be low

    private readonly FerrantiUla5C6C _ula;
    private readonly PortDecoder     _decoder;

    public ZxSpectrumPortBus(FerrantiUla5C6C ula, KempstonJoystick joystick)
    {
        _ula = ula;
        
        // Use LogicalAnd policy for hardware-accurate collisions (TD-019 / TD-038)
        _decoder = new PortDecoder(PortDecoder.ConflictPolicy.LogicalAnd);
        
        // ULA responds to A0 low. Receives full 16-bit address for keyboard scanning.
        _decoder.MapMirror(0x0000, Ula_Decode_Mask, 0xFFFF, ula);
        
        // Kempston Joystick responds to A5 low. Historically port $1F. 
        // Data is always on bits 0-4 regardless of high byte.
        _decoder.MapMirror(0x0000, Kempston_Decode_Mask, 0x0000, joystick);
    }

    public byte In(ushort port)
    {
        // Update OpenBusValue to current ULA floating bus state before reading
        _decoder.OpenBusValue = _ula.FloatingBusValue;
        return _decoder.In(port);
    }

    public void Out(ushort port, byte value) => _decoder.Out(port, value);
}
