using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// Maps IPhysicalKeyboard state to Sinclair 8×5 matrix bytes.
/// Used by ZX80, ZX81, and partially by the ZX Spectrum.
///
/// The Sinclair keyboard is an 8×5 matrix. Each half-row is selected by pulling
/// one address line (A8–A15) low. The result byte has bits 0–4 low for each
/// pressed key; bits 5–7 are always 1.
/// </summary>
public sealed class SinclairKeyboardAdapter
{
    private readonly IPhysicalKeyboard _keyboard;

    // Each row: array of 5 PhysicalKeys for bits 0–4.
    // Index matches address line: 0=A8, 1=A9, ..., 7=A15.
    private static readonly PhysicalKey[][] Rows =
    [
        // A8  (high byte bit 0 low → highByte & 0x01 == 0)
        [PhysicalKey.LeftShift, PhysicalKey.Z, PhysicalKey.X, PhysicalKey.C, PhysicalKey.V],
        // A9
        [PhysicalKey.A, PhysicalKey.S, PhysicalKey.D, PhysicalKey.F, PhysicalKey.G],
        // A10
        [PhysicalKey.Q, PhysicalKey.W, PhysicalKey.E, PhysicalKey.R, PhysicalKey.T],
        // A11
        [PhysicalKey.D1, PhysicalKey.D2, PhysicalKey.D3, PhysicalKey.D4, PhysicalKey.D5],
        // A12
        [PhysicalKey.D0, PhysicalKey.D9, PhysicalKey.D8, PhysicalKey.D7, PhysicalKey.D6],
        // A13
        [PhysicalKey.P, PhysicalKey.O, PhysicalKey.I, PhysicalKey.U, PhysicalKey.Y],
        // A14
        [PhysicalKey.Return, PhysicalKey.L, PhysicalKey.K, PhysicalKey.J, PhysicalKey.H],
        // A15
        [PhysicalKey.Space, PhysicalKey.RightShift, PhysicalKey.M, PhysicalKey.N, PhysicalKey.B],
    ];

    public SinclairKeyboardAdapter(IPhysicalKeyboard keyboard) => _keyboard = keyboard;

    /// <summary>
    /// Read the keyboard result for the given port high byte.
    /// Bits 0–7 of highByte correspond to address lines A8–A15; a 0 bit selects that row.
    /// Returns a byte with bits 0–4 low for pressed keys, bits 5–7 always 1.
    /// </summary>
    public byte Read(byte highByte)
    {
        byte result = 0xFF;
        for (int row = 0; row < 8; row++)
        {
            if ((highByte & (1 << row)) != 0) continue; // this row not selected
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
                b &= (byte)~(1 << bit); // pull bit low
        }
        return b;
    }
}
