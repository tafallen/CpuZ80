using CpuZ80.Core;

namespace Machines.ZxSpectrum;

/// <summary>
/// Routes I/O traffic between the ULA and the Kempston interface.
/// </summary>
internal sealed class ZxSpectrumPortBus : IPortBus
{
    private readonly FerrantiUla5C6C _ula;
    private readonly KempstonJoystick _joystick;

    public ZxSpectrumPortBus(FerrantiUla5C6C ula, KempstonJoystick joystick)
    {
        _ula = ula;
        _joystick = joystick;
    }

    public byte In(ushort port)
    {
        // 1. Kempston Joystick: responds to Port $1F (A5, A6, A7 low)
        // Historically, many interfaces only checked A5 low.
        if ((port & 0x0020) == 0)
        {
            return _joystick.In(port);
        }

        // 2. ULA: responds to any even port address (A0 low)
        if ((port & 0x0001) == 0)
        {
            return _ula.In(port);
        }

        // 3. Floating Bus
        return _ula.FloatingBusValue;
    }

    public void Out(ushort port, byte value)
    {
        // ULA handles Port $FE writes
        if ((port & 0x0001) == 0)
        {
            _ula.Out(port, value);
        }
    }
}
