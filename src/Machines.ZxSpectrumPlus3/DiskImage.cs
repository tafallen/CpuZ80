using System.Text;

namespace Machines.ZxSpectrumPlus3;

/// <summary>
/// A floppy disk loaded from a CPCEMU <c>.DSK</c> image, in either the standard
/// or the extended variant.
/// </summary>
/// <remarks>
/// Pure data: this knows about tracks and sectors but nothing about the
/// controller. See docs/upd765a-fdc.md §4.
///
/// Writes land in the in-memory sector buffers, so they persist for the session
/// and are then lost: nothing writes the image back to disk yet. See the
/// silent-data-loss risk in docs/upd765a-fdc.md §6.
/// </remarks>
public sealed class DiskImage
{
    private const int HeaderSize = 256;
    private const string StandardSignature = "MV - CPC";
    private const string ExtendedSignature = "EXTENDED";

    /// <summary>One sector, as described by its entry in the track's sector list.</summary>
    public sealed class Sector
    {
        /// <summary>Cylinder recorded in the sector's ID field.</summary>
        public byte C { get; init; }

        /// <summary>Head recorded in the sector's ID field.</summary>
        public byte H { get; init; }

        /// <summary>Sector number recorded in the ID field. Not an index — +3 disks number these 0x41+ or 0xC1+.</summary>
        public byte R { get; init; }

        /// <summary>Size code: the sector holds <c>128 &lt;&lt; N</c> bytes.</summary>
        public byte N { get; init; }

        /// <summary>ST1 as recorded when the image was made — carries deliberate errors for copy protection.</summary>
        public byte St1 { get; init; }

        /// <summary>ST2 as recorded when the image was made.</summary>
        /// <remarks>
        /// Settable because writing deleted data changes it: the control mark is
        /// a property of the sector on the disk, not of the image file.
        /// </remarks>
        public byte St2 { get; set; }

        /// <summary>The sector's data. Writes go here.</summary>
        public byte[] Data { get; init; } = [];

        /// <summary>True when the image marked this sector as deleted data (ST2 bit 6).</summary>
        public bool IsDeleted
        {
            get => (St2 & 0x40) != 0;
            set => St2 = value ? (byte)(St2 | 0x40) : (byte)(St2 & ~0x40);
        }
    }

    public sealed class Track
    {
        public byte Number { get; init; }
        public byte Side { get; init; }

        /// <summary>
        /// The sectors on this track. Replaced wholesale by a format, which is
        /// the one operation that changes a track's shape rather than its
        /// contents.
        /// </summary>
        public IReadOnlyList<Sector> Sectors { get; internal set; } = [];

        /// <summary>An unformatted track holds no sectors at all, which is not the same as holding empty ones.</summary>
        public bool IsUnformatted => Sectors.Count == 0;
    }

    public int TrackCount { get; }
    public int SideCount { get; }
    public bool IsExtended { get; }

    /// <summary>Set when the image was opened read-only; the FDC reports write-protected.</summary>
    public bool IsWriteProtected { get; set; }

    private readonly Track[] _tracks;   // indexed [track * SideCount + side]

    public DiskImage(byte[] image)
    {
        if (image.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"A .DSK image needs at least a {HeaderSize}-byte header, got {image.Length}.", nameof(image));
        }

        // Only the first eight bytes of the signature are dependable — emulators
        // have written all sorts of things into the rest of it.
        string signature = Encoding.ASCII.GetString(image, 0, 8);
        IsExtended = signature == ExtendedSignature;

        if (!IsExtended && signature != StandardSignature)
        {
            throw new ArgumentException(
                $"Not a .DSK image: signature was \"{signature}\".", nameof(image));
        }

        TrackCount = image[0x30];
        SideCount = image[0x31];

        if (SideCount is < 1 or > 2)
        {
            throw new ArgumentException($"A disk has one or two sides, the image claims {SideCount}.", nameof(image));
        }

        _tracks = new Track[TrackCount * SideCount];

        int offset = HeaderSize;
        for (int t = 0; t < TrackCount; t++)
        {
            for (int s = 0; s < SideCount; s++)
            {
                int index = t * SideCount + s;
                int size = TrackSize(image, index);

                // A zero-length track in the extended table is unformatted, and
                // occupies no space in the file at all.
                if (size == 0)
                {
                    _tracks[index] = new Track { Number = (byte)t, Side = (byte)s };
                    continue;
                }

                if (offset + size > image.Length)
                {
                    throw new ArgumentException(
                        $"Image is truncated: track {t} side {s} needs {size} bytes at offset {offset}, " +
                        $"but the file is {image.Length} bytes.", nameof(image));
                }

                _tracks[index] = ParseTrack(image, offset, (byte)t, (byte)s);
                offset += size;
            }
        }
    }

    private int TrackSize(byte[] image, int trackIndex)
    {
        if (!IsExtended) return image[0x32] | (image[0x33] << 8);

        // Extended images carry one byte per track, in units of 256.
        return image[0x34 + trackIndex] * 256;
    }

    private Track ParseTrack(byte[] image, int offset, byte expectedTrack, byte expectedSide)
    {
        if (Encoding.ASCII.GetString(image, offset, 10) != "Track-Info")
        {
            throw new ArgumentException(
                $"Expected a Track-Info block at offset {offset} for track {expectedTrack} side {expectedSide}.",
                nameof(image));
        }

        byte trackNumber = image[offset + 0x10];
        byte side = image[offset + 0x11];
        byte sectorCount = image[offset + 0x15];

        var sectors = new List<Sector>(sectorCount);
        int dataOffset = offset + HeaderSize;

        for (int i = 0; i < sectorCount; i++)
        {
            int info = offset + 0x18 + i * 8;

            byte n = image[info + 3];
            int declared = image[info + 6] | (image[info + 7] << 8);

            // The standard format's length field is unreliable, so the size code
            // is authoritative there. In extended images the reverse is true:
            // the declared length is the only way to express a weak or
            // over-long sector, which copy protection relies on.
            int length = IsExtended && declared > 0 ? declared : 128 << n;

            byte[] data = new byte[length];
            int available = Math.Min(length, image.Length - dataOffset);
            if (available > 0) Array.Copy(image, dataOffset, data, 0, available);

            sectors.Add(new Sector
            {
                C = image[info + 0],
                H = image[info + 1],
                R = image[info + 2],
                N = n,
                St1 = image[info + 4],
                St2 = image[info + 5],
                Data = data,
            });

            dataOffset += length;
        }

        return new Track { Number = trackNumber, Side = side, Sectors = sectors };
    }

    /// <summary>The track at this physical position, or null if the disk does not have one.</summary>
    public Track? GetTrack(int track, int side)
    {
        if (track < 0 || track >= TrackCount) return null;
        if (side < 0 || side >= SideCount) return null;
        return _tracks[track * SideCount + side];
    }

    /// <summary>
    /// Rewrites a track's sector list, as a format does.
    /// </summary>
    /// <remarks>
    /// Formatting is the only operation that changes geometry: sector count,
    /// numbering and size all come from what the controller was told to lay
    /// down, not from what was there before.
    /// </remarks>
    public bool FormatTrack(int track, int side, IReadOnlyList<Sector> sectors)
    {
        var existing = GetTrack(track, side);
        if (existing is null) return false;

        existing.Sectors = sectors;
        return true;
    }

    /// <summary>
    /// Finds a sector by its recorded ID, the way the hardware does — by
    /// searching the track rather than indexing into it.
    /// </summary>
    /// <remarks>
    /// Indexing by position would work only for sequentially numbered,
    /// non-interleaved disks, and +3 disks are neither: a system disk numbers
    /// its sectors 0x41-0x49 and a data disk 0xC1-0xC9.
    /// </remarks>
    public Sector? FindSector(int track, int side, byte r)
    {
        var t = GetTrack(track, side);
        if (t is null) return null;

        foreach (var sector in t.Sectors)
        {
            if (sector.R == r) return sector;
        }
        return null;
    }
}
