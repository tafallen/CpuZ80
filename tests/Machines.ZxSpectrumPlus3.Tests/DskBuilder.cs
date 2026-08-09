using System.Text;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// Builds valid <c>.DSK</c> images in memory, byte by byte.
/// </summary>
/// <remarks>
/// We have no real disk images, so the alternative to this is testing the
/// parser against nothing. A synthetic disk is a stronger test than a real one
/// for everything except compatibility with other emulators' quirks — the
/// layout is known exactly, so a mis-parse is unambiguous rather than a guess.
/// It is not a substitute for loading a real game. See docs/upd765a-fdc.md §6.
/// </remarks>
internal static class DskBuilder
{
    public const int SectorSize = 512;
    public const byte SectorSizeCode = 2;      // 128 << 2

    /// <summary>+3 system-format disks number their sectors from 0x41.</summary>
    public const byte SystemFirstSector = 0x41;

    /// <summary>+3 data-format disks number theirs from 0xC1.</summary>
    public const byte DataFirstSector = 0xC1;

    /// <summary>
    /// A standard-format image: <paramref name="tracks"/> tracks of
    /// <paramref name="sectorsPerTrack"/> sectors, numbered from
    /// <paramref name="firstSector"/>.
    /// </summary>
    /// <remarks>
    /// Each sector is filled with a byte derived from its track and number, so a
    /// test that reads the wrong sector gets an obviously wrong value rather
    /// than a plausible one.
    /// </remarks>
    public static byte[] Standard(
        int tracks = 40,
        int sides = 1,
        int sectorsPerTrack = 9,
        byte firstSector = SystemFirstSector)
    {
        int trackSize = 256 + sectorsPerTrack * SectorSize;
        var image = new List<byte>();

        image.AddRange(DiskHeader("MV - CPCEMU Disk-File\r\nDisk-Info\r\n", tracks, sides));
        image[0x32] = (byte)(trackSize & 0xFF);
        image[0x33] = (byte)(trackSize >> 8);

        for (int t = 0; t < tracks; t++)
        {
            for (int s = 0; s < sides; s++)
            {
                image.AddRange(TrackBlock(t, s, sectorsPerTrack, firstSector));
            }
        }

        return [.. image];
    }

    /// <summary>
    /// An extended-format image. <paramref name="unformattedTracks"/> lists
    /// tracks recorded as absent — size 0 in the table, occupying no file space.
    /// </summary>
    public static byte[] Extended(
        int tracks = 40,
        int sides = 1,
        int sectorsPerTrack = 9,
        byte firstSector = SystemFirstSector,
        params int[] unformattedTracks)
    {
        int trackSize = 256 + sectorsPerTrack * SectorSize;
        var image = new List<byte>();

        image.AddRange(DiskHeader("EXTENDED CPC DSK File\r\nDisk-Info\r\n", tracks, sides));

        for (int t = 0; t < tracks; t++)
        {
            for (int s = 0; s < sides; s++)
            {
                bool absent = unformattedTracks.Contains(t);
                image[0x34 + t * sides + s] = absent ? (byte)0 : (byte)(trackSize / 256);
            }
        }

        for (int t = 0; t < tracks; t++)
        {
            if (unformattedTracks.Contains(t)) continue;

            for (int s = 0; s < sides; s++)
            {
                image.AddRange(TrackBlock(t, s, sectorsPerTrack, firstSector));
            }
        }

        return [.. image];
    }

    /// <summary>The byte a given sector is filled with, so tests can assert they read the right one.</summary>
    public static byte FillFor(int track, byte sector) => (byte)(track * 0x10 + (sector & 0x0F));

    private static List<byte> DiskHeader(string signature, int tracks, int sides)
    {
        var header = new List<byte>(new byte[256]);
        byte[] text = Encoding.ASCII.GetBytes(signature);
        for (int i = 0; i < text.Length; i++) header[i] = text[i];

        header[0x30] = (byte)tracks;
        header[0x31] = (byte)sides;
        return header;
    }

    private static List<byte> TrackBlock(int track, int side, int sectorsPerTrack, byte firstSector)
    {
        var block = new List<byte>(new byte[256]);

        byte[] text = Encoding.ASCII.GetBytes("Track-Info\r\n");
        for (int i = 0; i < text.Length; i++) block[i] = text[i];

        block[0x10] = (byte)track;
        block[0x11] = (byte)side;
        block[0x14] = SectorSizeCode;
        block[0x15] = (byte)sectorsPerTrack;
        block[0x17] = 0xE5;   // filler

        for (int i = 0; i < sectorsPerTrack; i++)
        {
            int info = 0x18 + i * 8;
            block[info + 0] = (byte)track;                      // C
            block[info + 1] = (byte)side;                       // H
            block[info + 2] = (byte)(firstSector + i);          // R
            block[info + 3] = SectorSizeCode;                   // N
            block[info + 4] = 0;                                // ST1
            block[info + 5] = 0;                                // ST2
            block[info + 6] = (byte)(SectorSize & 0xFF);
            block[info + 7] = (byte)(SectorSize >> 8);
        }

        for (int i = 0; i < sectorsPerTrack; i++)
        {
            byte fill = FillFor(track, (byte)(firstSector + i));
            block.AddRange(Enumerable.Repeat(fill, SectorSize));
        }

        return block;
    }
}
