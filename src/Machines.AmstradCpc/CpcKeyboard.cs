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
    /// The matrix, line by line, bit 0 first. Null means the CPC has a key there
    /// with no sensible equivalent on a modern keyboard.
    /// </summary>
    /// <remarks>
    /// Taken from the published matrix rather than reconstructed. An earlier
    /// version of this table was invented and was wrong in almost every
    /// position — with no key ever pressed, nothing noticed.
    /// </remarks>
    private static readonly PhysicalKey?[][] Matrix =
    [
        // Line 0: cursors, f9, f6, f3, keypad Enter, f.
        // Bit 6 is the keypad's ENTER, a different key from RETURN on line 2.
        // Mapping both to the host's Return would clear two bits at once, which
        // no real keypress does.
        [PhysicalKey.Up, PhysicalKey.Right, PhysicalKey.Down, PhysicalKey.F9,
         PhysicalKey.F6, PhysicalKey.F3, null, null],
        // Line 1: cursor left, Copy, f7, f8, f5, f1, f2, f0
        [PhysicalKey.Left, PhysicalKey.Insert, PhysicalKey.F7, PhysicalKey.F8,
         PhysicalKey.F5, PhysicalKey.F1, PhysicalKey.F2, PhysicalKey.F10],
        // Line 2: Clr, [, Return, ], f4, Shift, \, Ctrl
        [PhysicalKey.Delete, PhysicalKey.LeftBracket, PhysicalKey.Return, PhysicalKey.RightBracket,
         PhysicalKey.F4, PhysicalKey.LeftShift, PhysicalKey.Backslash, PhysicalKey.LeftControl],
        // Line 3: ^, -, @, P, ;, :, /, .
        [null, PhysicalKey.Minus, PhysicalKey.Grave, PhysicalKey.P,
         PhysicalKey.Semicolon, PhysicalKey.Apostrophe, PhysicalKey.Slash, PhysicalKey.Period],
        // Line 4: 0, 9, O, I, L, K, M, ,
        [PhysicalKey.D0, PhysicalKey.D9, PhysicalKey.O, PhysicalKey.I,
         PhysicalKey.L, PhysicalKey.K, PhysicalKey.M, PhysicalKey.Comma],
        // Line 5: 8, 7, U, Y, H, J, N, Space
        [PhysicalKey.D8, PhysicalKey.D7, PhysicalKey.U, PhysicalKey.Y,
         PhysicalKey.H, PhysicalKey.J, PhysicalKey.N, PhysicalKey.Space],
        // Line 6: 6, 5, R, T, G, F, B, V
        [PhysicalKey.D6, PhysicalKey.D5, PhysicalKey.R, PhysicalKey.T,
         PhysicalKey.G, PhysicalKey.F, PhysicalKey.B, PhysicalKey.V],
        // Line 7: 4, 3, E, W, S, D, C, X
        [PhysicalKey.D4, PhysicalKey.D3, PhysicalKey.E, PhysicalKey.W,
         PhysicalKey.S, PhysicalKey.D, PhysicalKey.C, PhysicalKey.X],
        // Line 8: 1, 2, Esc, Q, Tab, A, Caps Lock, Z
        [PhysicalKey.D1, PhysicalKey.D2, PhysicalKey.Escape, PhysicalKey.Q,
         PhysicalKey.Tab, PhysicalKey.A, PhysicalKey.CapsLock, PhysicalKey.Z],
        // Line 9: joystick 0, then Del
        [null, null, null, null, null, null, null, PhysicalKey.Backspace],
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
