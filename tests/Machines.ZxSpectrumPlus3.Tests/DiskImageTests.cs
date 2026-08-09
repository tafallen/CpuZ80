using Xunit;
using System.Text;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// Parsing CPCEMU <c>.DSK</c> images, standard and extended.
/// </summary>
/// <remarks>See docs/upd765a-fdc.md §4.</remarks>
public class DiskImageTests
{
    [Fact]
    public void StandardImage_ParsesGeometry()
    {
        var disk = new DiskImage(DskBuilder.Standard());

        Assert.False(disk.IsExtended);
        Assert.Equal(40, disk.TrackCount);
        Assert.Equal(1, disk.SideCount);

        var track = disk.GetTrack(0, 0);
        Assert.NotNull(track);
        Assert.Equal(9, track!.Sectors.Count);
        Assert.All(track.Sectors, s => Assert.Equal(512, s.Data.Length));
    }

    [Fact]
    public void ExtendedImage_ParsesGeometry()
    {
        var disk = new DiskImage(DskBuilder.Extended());

        Assert.True(disk.IsExtended);
        Assert.Equal(40, disk.TrackCount);
        Assert.Equal(9, disk.GetTrack(10, 0)!.Sectors.Count);
    }

    [Fact]
    public void SectorsKeepTheirRecordedNumbering()
    {
        // +3DOS tells a system disk from a data disk by reading a sector ID, so
        // the numbering has to survive parsing rather than being normalised.
        var system = new DiskImage(DskBuilder.Standard(firstSector: DskBuilder.SystemFirstSector));
        var data = new DiskImage(DskBuilder.Standard(firstSector: DskBuilder.DataFirstSector));

        Assert.Equal(0x41, system.GetTrack(0, 0)!.Sectors[0].R);
        Assert.Equal(0xC1, data.GetTrack(0, 0)!.Sectors[0].R);
    }

    [Fact]
    public void EachSectorHoldsItsOwnData()
    {
        var disk = new DiskImage(DskBuilder.Standard());

        for (int t = 0; t < 3; t++)
        {
            for (byte r = 0x41; r < 0x4A; r++)
            {
                var sector = disk.FindSector(t, 0, r);
                Assert.NotNull(sector);
                Assert.All(sector!.Data, b => Assert.Equal(DskBuilder.FillFor(t, r), b));
            }
        }
    }

    [Fact]
    public void FindSector_SearchesByIdNotByPosition()
    {
        // Interleaved and non-sequential numbering is normal, so indexing by
        // position would quietly return the wrong sector.
        var disk = new DiskImage(DskBuilder.Standard());

        Assert.Equal(0x45, disk.FindSector(0, 0, 0x45)!.R);
        Assert.Null(disk.FindSector(0, 0, 0x01));
        Assert.Null(disk.FindSector(0, 0, 0xC1));   // a data-disk number on a system disk
    }

    [Fact]
    public void UnformattedTrack_HasNoSectorsAndIsNotEmptyData()
    {
        var disk = new DiskImage(DskBuilder.Extended(tracks: 5, unformattedTracks: [2]));

        var unformatted = disk.GetTrack(2, 0);
        Assert.NotNull(unformatted);
        Assert.True(unformatted!.IsUnformatted);
        Assert.Empty(unformatted.Sectors);

        // The tracks after it must still line up — an absent track occupies no
        // file space, so mis-handling it shifts everything that follows.
        Assert.False(disk.GetTrack(3, 0)!.IsUnformatted);
        Assert.Equal(3, disk.GetTrack(3, 0)!.Number);
        Assert.All(disk.FindSector(3, 0, 0x41)!.Data,
            b => Assert.Equal(DskBuilder.FillFor(3, 0x41), b));
    }

    [Fact]
    public void WritesToASectorPersist()
    {
        var disk = new DiskImage(DskBuilder.Standard());

        disk.FindSector(5, 0, 0x43)!.Data[100] = 0x99;

        Assert.Equal(0x99, disk.FindSector(5, 0, 0x43)!.Data[100]);
    }

    [Fact]
    public void DeletedDataIsFlaggedFromSt2()
    {
        byte[] image = DskBuilder.Standard(tracks: 1);
        // Set ST2 bit 6 on the first sector's info entry: 256-byte disk header,
        // then 0x18 into the track header, byte 5 of the entry.
        image[256 + 0x18 + 5] = 0x40;

        var disk = new DiskImage(image);

        Assert.True(disk.GetTrack(0, 0)!.Sectors[0].IsDeleted);
        Assert.False(disk.GetTrack(0, 0)!.Sectors[1].IsDeleted);
    }

    [Fact]
    public void ExtendedImage_UsesTheDeclaredLengthNotTheSizeCode()
    {
        // Only the declared length can express a weak or over-long sector, which
        // is how copy protection works. In a standard image it is unreliable and
        // the size code wins.
        byte[] image = DskBuilder.Extended(tracks: 1, sectorsPerTrack: 1);
        image[256 + 0x18 + 6] = 0x00;
        image[256 + 0x18 + 7] = 0x01;   // declare 256 bytes despite N = 2

        var disk = new DiskImage(image);

        Assert.Equal(256, disk.GetTrack(0, 0)!.Sectors[0].Data.Length);
    }

    [Fact]
    public void StandardImage_IgnoresTheDeclaredLength()
    {
        byte[] image = DskBuilder.Standard(tracks: 1, sectorsPerTrack: 1);
        image[256 + 0x18 + 6] = 0x00;
        image[256 + 0x18 + 7] = 0x01;   // a standard image's length field is not trustworthy

        var disk = new DiskImage(image);

        Assert.Equal(512, disk.GetTrack(0, 0)!.Sectors[0].Data.Length);
    }

    // ── Rejection ────────────────────────────────────────────────────────────

    [Fact]
    public void RejectsSomethingThatIsNotADisk()
    {
        var ex = Assert.Throws<ArgumentException>(() => new DiskImage(new byte[512]));
        Assert.Contains("Not a .DSK image", ex.Message);
    }

    [Fact]
    public void RejectsATruncatedFile()
    {
        Assert.Throws<ArgumentException>(() => new DiskImage(new byte[64]));

        byte[] truncated = DskBuilder.Standard(tracks: 10)[..1000];
        Assert.Throws<ArgumentException>(() => new DiskImage(truncated));
    }

    [Fact]
    public void AcceptsASignatureWithAnUnusualTail()
    {
        // Emulators have written all sorts of things past the first eight bytes,
        // so only those are checked.
        byte[] image = DskBuilder.Standard(tracks: 1);
        byte[] junk = Encoding.ASCII.GetBytes("MV - CPCsomething entirely else\r\n");
        Array.Copy(junk, 0, image, 0, junk.Length);

        var disk = new DiskImage(image);

        Assert.Equal(1, disk.TrackCount);
    }
}
