namespace Machines.AmstradCpc;

/// <summary>
/// Detects and strips the 128-byte AMSDOS header some CPC ROM images carry.
/// </summary>
/// <remarks>
/// The images in this repo are headered: <c>Z80CPC.ROM</c> is 32,896 bytes
/// rather than 32,768 and <c>Z80DISK.ROM</c> is 16,512 rather than 16,384.
/// Loading one as-is puts everything 128 bytes out of alignment and the machine
/// executes garbage from its first instruction.
///
/// Detection is deliberately conservative. A header is only recognised when the
/// size excess is exactly 128 bytes *and* the header's own fields agree with
/// that — a raw dump that happens to be 128 bytes over is far less likely than
/// a mis-detection corrupting a good image.
///
/// See docs/amstrad-cpc.md §8.
/// </remarks>
public static class AmsdosHeader
{
    public const int Size = 128;

    /// <summary>
    /// Returns the ROM payload, stripping an AMSDOS header if one is present.
    /// </summary>
    public static byte[] Strip(byte[] image)
    {
        return HasHeader(image) ? image[Size..] : image;
    }

    /// <summary>
    /// True when <paramref name="image"/> begins with an AMSDOS header.
    /// </summary>
    public static bool HasHeader(byte[] image)
    {
        if (image.Length <= Size) return false;

        // The payload must be a whole number of 16K ROMs once the header is
        // removed, and must not already be one with the header included.
        int payload = image.Length - Size;
        if (payload % 0x4000 != 0) return false;
        if (image.Length % 0x4000 == 0) return false;

        // File type 2 is "binary". Anything else is not a ROM image with a
        // header on it.
        if (image[0x12] != 0x02) return false;

        // The logical length at 0x18 and the 24-bit file length at 0x40 both
        // describe the payload. Requiring one of them to match rules out a raw
        // dump whose bytes coincidentally look like a header.
        int logicalLength = image[0x18] | (image[0x19] << 8);
        int fileLength = image[0x40] | (image[0x41] << 8) | (image[0x42] << 16);

        return logicalLength == payload || fileLength == payload;
    }
}
