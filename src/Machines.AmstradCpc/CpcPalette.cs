namespace Machines.AmstradCpc;

/// <summary>
/// The CPC's hardware palette: 32 selectable values covering 27 distinct
/// colours, as RGBA32.
/// </summary>
/// <remarks>
/// Each channel is one of three levels — off, half, full — giving 3³ = 27
/// colours. The Gate Array accepts a 5-bit value, so several of the 32 codes
/// are duplicates.
///
/// The <see cref="IVideoSink"/> contract in this repo is RGBA32 with the alpha
/// in the top byte, matching the Sinclair machines.
/// </remarks>
public static class CpcPalette
{
    private const byte Off = 0x00;
    private const byte Half = 0x80;
    private const byte Full = 0xFF;

    /// <summary>Hardware colour code (0-31) to RGBA32.</summary>
    public static readonly uint[] Colours = BuildColours();

    public static uint ToRgba(int hardwareColour) => Colours[hardwareColour & 0x1F];

    private static uint Rgba(byte r, byte g, byte b) =>
        0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;

    private static uint[] BuildColours()
    {
        // Firmware colour order, as the hardware numbers them. Written out
        // rather than computed: the mapping from code to RGB level is not a
        // simple bit split, and inventing a formula would silently produce a
        // plausible-looking but wrong palette.
        (byte R, byte G, byte B)[] table =
        [
            (Half, Half, Half),   // 0  white
            (Half, Half, Half),   // 1  white (duplicate)
            (Off,  Full, Half),   // 2  sea green
            (Full, Full, Half),   // 3  pastel yellow
            (Off,  Off,  Half),   // 4  blue
            (Full, Off,  Half),   // 5  purple
            (Off,  Half, Half),   // 6  cyan
            (Full, Half, Half),   // 7  pink
            (Full, Off,  Half),   // 8  purple (duplicate)
            (Full, Full, Half),   // 9  pastel yellow (duplicate)
            (Full, Full, Off),    // 10 bright yellow
            (Full, Full, Full),   // 11 bright white
            (Full, Off,  Off),    // 12 bright red
            (Full, Off,  Full),   // 13 bright magenta
            (Full, Half, Off),    // 14 orange
            (Full, Half, Full),   // 15 pastel magenta
            (Off,  Off,  Half),   // 16 blue (duplicate)
            (Off,  Full, Half),   // 17 sea green (duplicate)
            (Off,  Full, Off),    // 18 bright green
            (Off,  Full, Full),   // 19 bright cyan
            (Off,  Off,  Off),    // 20 black
            (Off,  Off,  Full),   // 21 bright blue
            (Off,  Half, Off),    // 22 green
            (Off,  Half, Full),   // 23 sky blue
            (Half, Off,  Half),   // 24 magenta
            (Half, Full, Half),   // 25 pastel green
            (Half, Full, Off),    // 26 lime
            (Half, Full, Full),   // 27 pastel cyan
            (Half, Off,  Off),    // 28 red
            (Half, Off,  Full),   // 29 mauve
            (Half, Half, Off),    // 30 yellow
            (Half, Half, Full),   // 31 pastel blue
        ];

        var colours = new uint[32];
        for (int i = 0; i < colours.Length; i++)
        {
            colours[i] = Rgba(table[i].R, table[i].G, table[i].B);
        }
        return colours;
    }
}
