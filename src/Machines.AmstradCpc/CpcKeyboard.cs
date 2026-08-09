using Machines.Common;

namespace Machines.AmstradCpc;

/// <summary>
/// Maps a host keyboard onto the CPC's ten-row matrix.
/// </summary>
/// <remarks>
/// Rows 0-8 are keys; row 9 is the joystick plus a few keys. Bits are active
/// low: a clear bit means pressed, and an unread row returns 0xFF.
/// </remarks>
public sealed class CpcKeyboard(IPhysicalKeyboard host) : ICpcKeyboard
{
    private readonly IPhysicalKeyboard _host = host;

    /// <summary>
    /// The matrix, row by row, bit 0 first. Null means no key at that position.
    /// </summary>
    private static readonly PhysicalKey?[][] Matrix =
    [
        // Row 0
        [PhysicalKey.Up, PhysicalKey.Right, PhysicalKey.Down, PhysicalKey.D9,
         PhysicalKey.D6, PhysicalKey.D3, PhysicalKey.Return, PhysicalKey.Period],
        // Row 1
        [PhysicalKey.Left, null, PhysicalKey.D7, PhysicalKey.D8,
         PhysicalKey.D5, PhysicalKey.D1, PhysicalKey.D2, PhysicalKey.D0],
        // Row 2
        [null, null, PhysicalKey.Escape, PhysicalKey.Y,
         PhysicalKey.U, PhysicalKey.R, PhysicalKey.T, PhysicalKey.O],
        // Row 3
        [null, null, PhysicalKey.Backslash, PhysicalKey.I,
         PhysicalKey.P, PhysicalKey.E, PhysicalKey.W, PhysicalKey.D4],
        // Row 4
        [null, null, PhysicalKey.RightBracket, PhysicalKey.K,
         PhysicalKey.L, PhysicalKey.S, PhysicalKey.Q, PhysicalKey.Tab],
        // Row 5
        [null, null, PhysicalKey.LeftBracket, PhysicalKey.J,
         PhysicalKey.H, PhysicalKey.D, PhysicalKey.A, PhysicalKey.CapsLock],
        // Row 6
        [null, null, PhysicalKey.Backspace, PhysicalKey.N,
         PhysicalKey.B, PhysicalKey.F, PhysicalKey.G, PhysicalKey.Z],
        // Row 7
        [null, null, PhysicalKey.Equals, PhysicalKey.M,
         PhysicalKey.Comma, PhysicalKey.C, PhysicalKey.X, PhysicalKey.Space],
        // Row 8
        [null, null, PhysicalKey.Minus, PhysicalKey.Semicolon,
         PhysicalKey.Slash, PhysicalKey.Apostrophe, PhysicalKey.LeftShift, PhysicalKey.LeftControl],
        // Row 9 — joystick and a few keys
        [null, null, null, null, null, null, null, null],
    ];

    public byte ReadRow(int row)
    {
        if (row < 0 || row >= Matrix.Length) return 0xFF;

        byte value = 0xFF;
        var keys = Matrix[row];

        for (int bit = 0; bit < 8; bit++)
        {
            var key = keys[bit];
            if (key is not null && _host.IsKeyDown(key.Value))
            {
                value &= (byte)~(1 << bit);
            }
        }

        return value;
    }
}

/// <summary>A keyboard with nothing pressed, for a machine built without one.</summary>
internal sealed class NullCpcKeyboard : ICpcKeyboard
{
    public static readonly NullCpcKeyboard Instance = new();

    public byte ReadRow(int row) => 0xFF;
}
