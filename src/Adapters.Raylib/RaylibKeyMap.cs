using Machines.Common;
using Raylib_cs;

namespace Adapters.Raylib;

/// <summary>Maps PhysicalKey values to Raylib KeyboardKey scancodes.</summary>
internal static class RaylibKeyMap
{
    private static readonly Dictionary<PhysicalKey, KeyboardKey> Map = new()
    {
        // Letters
        { PhysicalKey.A, KeyboardKey.A }, { PhysicalKey.B, KeyboardKey.B },
        { PhysicalKey.C, KeyboardKey.C }, { PhysicalKey.D, KeyboardKey.D },
        { PhysicalKey.E, KeyboardKey.E }, { PhysicalKey.F, KeyboardKey.F },
        { PhysicalKey.G, KeyboardKey.G }, { PhysicalKey.H, KeyboardKey.H },
        { PhysicalKey.I, KeyboardKey.I }, { PhysicalKey.J, KeyboardKey.J },
        { PhysicalKey.K, KeyboardKey.K }, { PhysicalKey.L, KeyboardKey.L },
        { PhysicalKey.M, KeyboardKey.M }, { PhysicalKey.N, KeyboardKey.N },
        { PhysicalKey.O, KeyboardKey.O }, { PhysicalKey.P, KeyboardKey.P },
        { PhysicalKey.Q, KeyboardKey.Q }, { PhysicalKey.R, KeyboardKey.R },
        { PhysicalKey.S, KeyboardKey.S }, { PhysicalKey.T, KeyboardKey.T },
        { PhysicalKey.U, KeyboardKey.U }, { PhysicalKey.V, KeyboardKey.V },
        { PhysicalKey.W, KeyboardKey.W }, { PhysicalKey.X, KeyboardKey.X },
        { PhysicalKey.Y, KeyboardKey.Y }, { PhysicalKey.Z, KeyboardKey.Z },

        // Number row
        { PhysicalKey.D0, KeyboardKey.Zero  }, { PhysicalKey.D1, KeyboardKey.One   },
        { PhysicalKey.D2, KeyboardKey.Two   }, { PhysicalKey.D3, KeyboardKey.Three },
        { PhysicalKey.D4, KeyboardKey.Four  }, { PhysicalKey.D5, KeyboardKey.Five  },
        { PhysicalKey.D6, KeyboardKey.Six   }, { PhysicalKey.D7, KeyboardKey.Seven },
        { PhysicalKey.D8, KeyboardKey.Eight }, { PhysicalKey.D9, KeyboardKey.Nine  },

        // Symbols
        { PhysicalKey.Minus,        KeyboardKey.Minus        },
        { PhysicalKey.Equals,       KeyboardKey.Equal        },
        { PhysicalKey.LeftBracket,  KeyboardKey.LeftBracket  },
        { PhysicalKey.RightBracket, KeyboardKey.RightBracket },
        { PhysicalKey.Semicolon,    KeyboardKey.Semicolon    },
        { PhysicalKey.Apostrophe,   KeyboardKey.Apostrophe   },
        { PhysicalKey.Comma,        KeyboardKey.Comma        },
        { PhysicalKey.Period,       KeyboardKey.Period        },
        { PhysicalKey.Slash,        KeyboardKey.Slash        },
        { PhysicalKey.Backslash,    KeyboardKey.Backslash    },
        { PhysicalKey.Grave,        KeyboardKey.Grave        },

        // Modifiers
        { PhysicalKey.LeftShift,    KeyboardKey.LeftShift    },
        { PhysicalKey.RightShift,   KeyboardKey.RightShift   },
        { PhysicalKey.LeftControl,  KeyboardKey.LeftControl  },
        { PhysicalKey.RightControl, KeyboardKey.RightControl },
        { PhysicalKey.CapsLock,     KeyboardKey.CapsLock     },

        // Editing
        { PhysicalKey.Space,        KeyboardKey.Space        },
        { PhysicalKey.Return,       KeyboardKey.Enter        },
        { PhysicalKey.Backspace,    KeyboardKey.Backspace    },
        { PhysicalKey.Delete,       KeyboardKey.Delete       },
        { PhysicalKey.Escape,       KeyboardKey.Escape       },
        { PhysicalKey.Tab,          KeyboardKey.Tab          },
        { PhysicalKey.Insert,       KeyboardKey.Insert       },
        { PhysicalKey.Home,         KeyboardKey.Home         },
        { PhysicalKey.End,          KeyboardKey.End          },
        { PhysicalKey.PageUp,       KeyboardKey.PageUp       },
        { PhysicalKey.PageDown,     KeyboardKey.PageDown     },

        // Cursor
        { PhysicalKey.Up,    KeyboardKey.Up    },
        { PhysicalKey.Down,  KeyboardKey.Down  },
        { PhysicalKey.Left,  KeyboardKey.Left  },
        { PhysicalKey.Right, KeyboardKey.Right },

        // Function keys
        { PhysicalKey.F1,  KeyboardKey.F1  }, { PhysicalKey.F2,  KeyboardKey.F2  },
        { PhysicalKey.F3,  KeyboardKey.F3  }, { PhysicalKey.F4,  KeyboardKey.F4  },
        { PhysicalKey.F5,  KeyboardKey.F5  }, { PhysicalKey.F6,  KeyboardKey.F6  },
        { PhysicalKey.F7,  KeyboardKey.F7  }, { PhysicalKey.F8,  KeyboardKey.F8  },
        { PhysicalKey.F9,  KeyboardKey.F9  }, { PhysicalKey.F10, KeyboardKey.F10 },
        { PhysicalKey.F11, KeyboardKey.F11 }, { PhysicalKey.F12, KeyboardKey.F12 },
    };

    public static bool TryGet(PhysicalKey key, out KeyboardKey raylibKey) =>
        Map.TryGetValue(key, out raylibKey);
}
