namespace Machines.Common;

/// <summary>
/// Physical key positions based on a standard US QWERTY layout.
/// Values are position-based, not character-based — key A is always key A
/// regardless of the OS keyboard locale or what character it produces.
/// </summary>
public enum PhysicalKey
{
    None,

    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Number row
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Symbols (top row / right side, position-based)
    Minus, Equals, LeftBracket, RightBracket,
    Semicolon, Apostrophe, Hash,
    Comma, Period, Slash, Backslash, Grave,

    // Modifiers
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt, CapsLock,

    // Editing
    Space, Return, Backspace, Delete, Escape, Tab, Insert, Home, End, PageUp, PageDown,

    // Cursor
    Up, Down, Left, Right,

    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}
