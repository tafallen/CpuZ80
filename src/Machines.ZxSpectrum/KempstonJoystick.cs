using Machines.Common;
using CpuZ80.Core;

namespace Machines.ZxSpectrum;

/// <summary>
/// Emulates the Kempston Joystick interface, returning state on port $1F.
/// Bit 0: Right, Bit 1: Left, Bit 2: Down, Bit 3: Up, Bit 4: Fire.
/// </summary>
public sealed class KempstonJoystick : IPortBus
{
    private readonly IPhysicalKeyboard? _input;

    public KempstonJoystick(IPhysicalKeyboard? input)
    {
        _input = input;
    }

    public byte In(ushort port)
    {
        if (_input == null) return 0x00;

        byte result = 0;
        if (_input.IsKeyDown(PhysicalKey.Right)) result |= 0x01;
        if (_input.IsKeyDown(PhysicalKey.Left))  result |= 0x02;
        if (_input.IsKeyDown(PhysicalKey.Down))  result |= 0x04;
        if (_input.IsKeyDown(PhysicalKey.Up))    result |= 0x08;
        if (_input.IsKeyDown(PhysicalKey.LeftControl) || _input.IsKeyDown(PhysicalKey.RightControl) || _input.IsKeyDown(PhysicalKey.LeftAlt)) 
            result |= 0x10;

        return result;
    }

    public void Out(ushort port, byte value) { }
}
