using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// Maps IPhysicalKeyboard state to Sinclair 8×5 matrix bytes.
/// Used by ZX80, ZX81, and the ZX Spectrum.
/// </summary>
public sealed class SinclairKeyboardAdapter
{
    private readonly IPhysicalKeyboard _keyboard;

    private static readonly PhysicalKey[][] Rows =
    [
        [PhysicalKey.LeftShift, PhysicalKey.Z, PhysicalKey.X, PhysicalKey.C, PhysicalKey.V],
        [PhysicalKey.A, PhysicalKey.S, PhysicalKey.D, PhysicalKey.F, PhysicalKey.G],
        [PhysicalKey.Q, PhysicalKey.W, PhysicalKey.E, PhysicalKey.R, PhysicalKey.T],
        [PhysicalKey.D1, PhysicalKey.D2, PhysicalKey.D3, PhysicalKey.D4, PhysicalKey.D5],
        [PhysicalKey.D0, PhysicalKey.D9, PhysicalKey.D8, PhysicalKey.D7, PhysicalKey.D6],
        [PhysicalKey.P, PhysicalKey.O, PhysicalKey.I, PhysicalKey.U, PhysicalKey.Y],
        [PhysicalKey.Return, PhysicalKey.L, PhysicalKey.K, PhysicalKey.J, PhysicalKey.H],
        [PhysicalKey.Space, PhysicalKey.RightShift, PhysicalKey.M, PhysicalKey.N, PhysicalKey.B],
    ];

    public SinclairKeyboardAdapter(IPhysicalKeyboard keyboard) => _keyboard = keyboard;

    /// <summary>
    /// Decodes the high byte of the port address to select rows and returns the key state.
    /// Bit 0 of portHigh corresponds to address line A8, Bit 7 to A15.
    /// </summary>
    public byte Read(ushort port)
    {
        byte portHigh = (byte)(port >> 8);
        byte result = 0xFF;
        for (int row = 0; row < 8; row++)
        {
            if ((portHigh & (1 << row)) != 0) continue; 
            result &= RowByte(Rows[row]);
        }
        return result;
    }

    private byte RowByte(PhysicalKey[] keys)
    {
        byte b = 0xFF;
        for (int bit = 0; bit < keys.Length; bit++)
        {
            if (_keyboard.IsKeyDown(keys[bit]))
                b &= (byte)~(1 << bit); 
        }
        return b;
    }
}
