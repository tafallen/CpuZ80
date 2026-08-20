using CpuZ80.Core;
using Machines.ZxSpectrumPlus3;

namespace Machines.AmstradCpc;

/// <summary>
/// The CPC's decoding for the floppy controller.
/// </summary>
/// <remarks>
/// The same uPD765A the +3 uses, but at completely different addresses: the CPC
/// puts it at <c>&amp;FA7E</c> and <c>&amp;FB7E</c> rather than 0x2FFD and
/// 0x3FFD, and adds a motor-control latch the +3 keeps in its paging port.
///
/// Selected when A10 is clear. A8 then chooses between the motor latch and the
/// controller, and A0 between the status and data registers.
/// </remarks>
internal sealed class CpcFdcPort(Upd765a fdc) : IPortBus
{
    private readonly Upd765a _fdc = fdc;

    /// <summary>A10 clear selects the disk hardware.</summary>
    private static bool IsDiskPort(ushort port) => (port & 0x0400) == 0;

    /// <summary>A8 set selects the controller; clear selects the motor latch.</summary>
    private static bool IsController(ushort port) => (port & 0x0100) != 0;

    /// <summary>A0 set selects the data register; clear selects main status.</summary>
    private static bool IsDataRegister(ushort port) => (port & 0x0001) != 0;

    public byte In(ushort port)
    {
        if (!IsDiskPort(port) || !IsController(port)) return 0xFF;

        return IsDataRegister(port) ? _fdc.ReadData() : _fdc.MainStatus;
    }

    public void Out(ushort port, byte value)
    {
        if (!IsDiskPort(port)) return;

        if (!IsController(port))
        {
            // The motor latch. Both drives run together on a CPC — there is one
            // motor line, not one per drive.
            _fdc.MotorOn = (value & 0x01) != 0;
            return;
        }

        if (IsDataRegister(port)) _fdc.WriteData(value);
    }
}
