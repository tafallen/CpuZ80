using Xunit;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// NEC uPD765A floppy controller — the three-phase command/execution/result
/// state machine behind ports 0x2FFD and 0x3FFD.
/// </summary>
/// <remarks>
/// These tests assert the Main Status Register transitions as well as the bytes.
/// A wrong status bit shows up as a disk error the user can see; a wrong
/// RQM/DIO transition hangs the driver silently, which is the more likely and
/// far worse failure. See docs/upd765a-fdc.md §6.
/// </remarks>
public class Upd765aTests
{
    private const ushort StatusPort = 0x2FFD;
    private const ushort DataPort = 0x3FFD;

    private const byte Rqm = 0x80;
    private const byte Dio = 0x40;
    private const byte Exm = 0x20;
    private const byte Cb  = 0x10;

    private static Upd765a Fdc(bool withDisk = true, bool motor = true, bool writeProtected = false)
    {
        var fdc = new Upd765a();
        if (withDisk)
        {
            var disk = new DiskImage(DskBuilder.Standard()) { IsWriteProtected = writeProtected };
            fdc.InsertDisk(0, disk);
        }
        fdc.MotorOn = motor;
        return fdc;
    }

    private static void Command(Upd765a fdc, params byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            // The driver polls before every byte, and must see the controller
            // willing to accept one.
            byte msr = fdc.In(StatusPort);
            Assert.True((msr & Rqm) != 0, $"RQM should be set before a command byte, MSR was 0x{msr:X2}");
            Assert.True((msr & Dio) == 0, $"DIO should be clear when the FDC expects input, MSR was 0x{msr:X2}");

            fdc.Out(DataPort, b);
        }
    }

    private static byte[] ReadResult(Upd765a fdc, int count)
    {
        var result = new byte[count];
        for (int i = 0; i < count; i++)
        {
            byte msr = fdc.In(StatusPort);
            Assert.True((msr & (Rqm | Dio | Cb)) == (Rqm | Dio | Cb),
                $"a result byte needs RQM, DIO and CB set, MSR was 0x{msr:X2}");
            result[i] = fdc.In(DataPort);
        }

        // The result phase must end exactly here: still polling for more would
        // read a byte the hardware never offers.
        byte after = fdc.In(StatusPort);
        Assert.True((after & Cb) == 0, $"the controller should be idle after the last result byte, MSR was 0x{after:X2}");
        return result;
    }

    // ── Idle and the status register ─────────────────────────────────────────

    [Fact]
    public void IdleControllerIsReadyAndNotBusy()
    {
        var fdc = Fdc();
        byte msr = fdc.In(StatusPort);

        Assert.Equal(Rqm, (byte)(msr & (Rqm | Dio | Exm | Cb)));
    }

    [Fact]
    public void StatusAndDataPortsDecodeOnA13A12AndA1()
    {
        var fdc = Fdc();

        Assert.Equal(fdc.MainStatus, fdc.In(0x2FFD));
        Assert.Equal(fdc.MainStatus, fdc.In(0x2000));   // A1 clear, same decode
        Assert.Equal(0xFF, fdc.In(0x2FFF));             // A1 set: not ours
        Assert.Equal(0xFF, fdc.In(0x0FFD));             // A13/A12 wrong
    }

    // ── Specify: the command with no result phase ────────────────────────────

    [Fact]
    public void Specify_ProducesNoResultBytes()
    {
        // The first command +3DOS issues. Offering it a result byte
        // desynchronises every command after it.
        var fdc = Fdc();

        Command(fdc, 0x03, 0xAF, 0x02);

        byte msr = fdc.In(StatusPort);
        Assert.True((msr & Cb) == 0, $"Specify has no result phase, but the FDC was busy: 0x{msr:X2}");
        Assert.True((msr & Dio) == 0, "Specify has no result phase, but the FDC offered output");
    }

    [Fact]
    public void SpecifyThenAnotherCommandStaysInStep()
    {
        // The symptom of a spurious Specify result is the *next* command
        // returning nonsense, so check the pair rather than Specify alone.
        var fdc = Fdc();

        Command(fdc, 0x03, 0xAF, 0x02);
        Command(fdc, 0x04, 0x00);                     // Sense Drive Status

        byte[] result = ReadResult(fdc, 1);
        Assert.NotEqual(0x00, result[0] & 0x20);      // RY: the drive is ready
    }

    // ── Seek and Sense Interrupt Status ──────────────────────────────────────

    [Fact]
    public void Recalibrate_ReturnsToTrackZeroAndReportsViaSenseInterrupt()
    {
        var fdc = Fdc();

        Command(fdc, 0x0F, 0x00, 20);    // Seek to track 20
        Command(fdc, 0x08);              // Sense Interrupt Status
        Assert.Equal(20, ReadResult(fdc, 2)[1]);

        Command(fdc, 0x07, 0x00);        // Recalibrate
        Command(fdc, 0x08);
        byte[] result = ReadResult(fdc, 2);

        Assert.Equal(0x20, result[0] & 0x20);   // SE, seek end
        Assert.Equal(0, result[1]);             // present cylinder
    }

    [Fact]
    public void Seek_HasNoResultPhaseOfItsOwn()
    {
        var fdc = Fdc();

        Command(fdc, 0x0F, 0x00, 5);

        byte msr = fdc.In(StatusPort);
        Assert.True((msr & Dio) == 0, "Seek reports through Sense Interrupt Status, not a result phase");
    }

    [Fact]
    public void SenseInterruptStatus_WithNothingPendingReportsInvalid()
    {
        // This is how the driver discovers it has drained every pending seek.
        var fdc = Fdc();

        Command(fdc, 0x08);

        Assert.Equal(0x80, ReadResult(fdc, 1)[0]);
    }

    [Fact]
    public void SenseDriveStatus_ReportsReadyTrackZeroAndWriteProtect()
    {
        var fdc = Fdc(writeProtected: true);

        Command(fdc, 0x04, 0x00);
        byte st3 = ReadResult(fdc, 1)[0];

        Assert.Equal(0x20, st3 & 0x20);   // RY
        Assert.Equal(0x10, st3 & 0x10);   // T0
        Assert.Equal(0x40, st3 & 0x40);   // WP
    }

    [Fact]
    public void MotorOff_MakesTheDriveNotReady()
    {
        var fdc = Fdc(motor: false);

        Command(fdc, 0x04, 0x00);

        Assert.Equal(0, ReadResult(fdc, 1)[0] & 0x20);   // RY clear
    }

    // ── Invalid commands ─────────────────────────────────────────────────────

    [Fact]
    public void UnknownOpcode_ReturnsTheInvalidCode()
    {
        // Ignoring it hangs the driver, which polls for a result that never
        // comes.
        var fdc = Fdc();

        Command(fdc, 0x1E);

        Assert.Equal(0x80, ReadResult(fdc, 1)[0]);
    }

    // ── Read ID ──────────────────────────────────────────────────────────────

    [Fact]
    public void ReadId_ReturnsTheRecordedSectorNumbering()
    {
        // +3DOS identifies the disk format from this, so a synthesised 1-to-9
        // would make every disk look like neither format.
        var fdc = Fdc();

        Command(fdc, 0x0A, 0x00);
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0, result[0] & 0xC0);          // IC: normal termination
        Assert.Equal(0, result[3]);                 // C
        Assert.Equal(0x41, result[5]);              // R, a system-format disk
        Assert.Equal(DskBuilder.SectorSizeCode, result[6]);
    }

    [Fact]
    public void ReadId_WalksTheTrack()
    {
        var fdc = Fdc();

        var seen = new List<byte>();
        for (int i = 0; i < 9; i++)
        {
            Command(fdc, 0x0A, 0x00);
            seen.Add(ReadResult(fdc, 7)[5]);
        }

        Assert.Equal(9, seen.Distinct().Count());
    }

    [Fact]
    public void ReadId_OnAnUnformattedTrackReportsAMissingAddressMark()
    {
        var fdc = new Upd765a { MotorOn = true };
        fdc.InsertDisk(0, new DiskImage(DskBuilder.Extended(tracks: 5, unformattedTracks: [2])));

        Command(fdc, 0x0F, 0x00, 2);    // seek to the unformatted track
        Command(fdc, 0x08);
        ReadResult(fdc, 2);

        Command(fdc, 0x0A, 0x00);
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0x40, result[0] & 0xC0);   // IC: abnormal
        Assert.Equal(0x01, result[1] & 0x01);   // ST1 MA
    }

    // ── Read Data ────────────────────────────────────────────────────────────

    [Fact]
    public void ReadData_TransfersTheSectorThenTheResultBytes()
    {
        var fdc = Fdc();

        Command(fdc, 0x0F, 0x00, 3);           // seek to track 3
        Command(fdc, 0x08);
        ReadResult(fdc, 2);

        // Read Data: opcode, drive/head, C, H, R, N, EOT, GPL, DTL
        Command(fdc, 0x46, 0x00, 3, 0, 0x42, 2, 0x49, 0x2A, 0xFF);

        var data = new byte[DskBuilder.SectorSize];
        for (int i = 0; i < data.Length; i++)
        {
            byte msr = fdc.In(StatusPort);
            Assert.True((msr & (Rqm | Dio | Exm | Cb)) == (Rqm | Dio | Exm | Cb),
                $"an execution-phase read needs RQM, DIO, EXM and CB, MSR was 0x{msr:X2}");
            data[i] = fdc.In(DataPort);
        }

        Assert.All(data, b => Assert.Equal(DskBuilder.FillFor(3, 0x42), b));

        byte[] result = ReadResult(fdc, 7);
        Assert.Equal(0, result[0] & 0xC0);
        Assert.Equal(3, result[3]);
        Assert.Equal(0x42, result[5]);
    }

    [Fact]
    public void ReadData_ForAMissingSectorReportsNoData()
    {
        var fdc = Fdc();

        Command(fdc, 0x46, 0x00, 0, 0, 0x77, 2, 0x49, 0x2A, 0xFF);
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0x40, result[0] & 0xC0);   // abnormal
        Assert.Equal(0x04, result[1] & 0x04);   // ST1 ND
    }

    [Fact]
    public void ReadData_WithNoDiskReportsNotReady()
    {
        var fdc = Fdc(withDisk: false);

        Command(fdc, 0x46, 0x00, 0, 0, 0x41, 2, 0x49, 0x2A, 0xFF);
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0x08, result[0] & 0x08);   // ST0 NR
    }

    // ── Write Data ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteData_StoresIntoTheImage()
    {
        var fdc = Fdc();

        Command(fdc, 0x45, 0x00, 0, 0, 0x43, 2, 0x49, 0x2A, 0xFF);

        for (int i = 0; i < DskBuilder.SectorSize; i++)
        {
            byte msr = fdc.In(StatusPort);
            Assert.True((msr & (Rqm | Exm | Cb)) == (Rqm | Exm | Cb),
                $"an execution-phase write needs RQM, EXM and CB, MSR was 0x{msr:X2}");
            Assert.True((msr & Dio) == 0, "DIO must be clear while the FDC expects data");
            fdc.Out(DataPort, (byte)(i & 0xFF));
        }

        ReadResult(fdc, 7);

        var sector = fdc.GetDisk(0)!.FindSector(0, 0, 0x43)!;
        Assert.Equal(0x00, sector.Data[0]);
        Assert.Equal(0x7B, sector.Data[123]);
    }

    [Fact]
    public void WriteData_ToAWriteProtectedDiskIsRefused()
    {
        var fdc = Fdc(writeProtected: true);

        Command(fdc, 0x45, 0x00, 0, 0, 0x43, 2, 0x49, 0x2A, 0xFF);
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0x40, result[0] & 0xC0);   // abnormal
        Assert.Equal(0x02, result[1] & 0x02);   // ST1 NW

        // And nothing was written.
        Assert.All(fdc.GetDisk(0)!.FindSector(0, 0, 0x43)!.Data,
            b => Assert.Equal(DskBuilder.FillFor(0, 0x43), b));
    }

    [Fact]
    public void WrittenDataReadsBack()
    {
        var fdc = Fdc();

        Command(fdc, 0x45, 0x00, 0, 0, 0x44, 2, 0x49, 0x2A, 0xFF);
        for (int i = 0; i < DskBuilder.SectorSize; i++) fdc.Out(DataPort, 0x5A);
        ReadResult(fdc, 7);

        Command(fdc, 0x46, 0x00, 0, 0, 0x44, 2, 0x49, 0x2A, 0xFF);
        var data = new byte[DskBuilder.SectorSize];
        for (int i = 0; i < data.Length; i++) data[i] = fdc.In(DataPort);
        ReadResult(fdc, 7);

        Assert.All(data, b => Assert.Equal(0x5A, b));
    }

    // ── Format ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormatTrack_LaysDownTheSectorsTheCpuSupplies()
    {
        // Format has an execution phase: the CPU sends C, H, R and N for every
        // sector it wants written. An earlier version of this test expected the
        // result bytes straight after the command, which is what a controller
        // that can only refill existing sectors would do — it can never change
        // a track's geometry, which is the whole point of formatting.
        var fdc = Fdc();

        // Format Track: opcode, drive/head, N, sectors/track, GPL, filler
        Command(fdc, 0x4D, 0x00, 2, 9, 0x52, 0xE5);

        for (int i = 0; i < 9; i++)
        {
            fdc.Out(DataPort, 0);                    // C
            fdc.Out(DataPort, 0);                    // H
            fdc.Out(DataPort, (byte)(0x41 + i));     // R
            fdc.Out(DataPort, 2);                    // N
        }

        ReadResult(fdc, 7);

        var track = fdc.GetDisk(0)!.GetTrack(0, 0)!;
        Assert.Equal(9, track.Sectors.Count);
        Assert.All(fdc.GetDisk(0)!.FindSector(0, 0, 0x41)!.Data, b => Assert.Equal(0xE5, b));
    }

    [Fact]
    public void FormatTrack_CanChangeTheTrackGeometry()
    {
        // Reformatting to a different sector count and numbering is exactly
        // what a controller that only refills sectors cannot do.
        var fdc = Fdc();

        Command(fdc, 0x4D, 0x00, 2, 4, 0x52, 0x00);
        for (int i = 0; i < 4; i++)
        {
            fdc.Out(DataPort, 0);
            fdc.Out(DataPort, 0);
            fdc.Out(DataPort, (byte)(0xC1 + i));     // data-format numbering
            fdc.Out(DataPort, 2);
        }
        ReadResult(fdc, 7);

        var track = fdc.GetDisk(0)!.GetTrack(0, 0)!;
        Assert.Equal(4, track.Sectors.Count);
        Assert.NotNull(fdc.GetDisk(0)!.FindSector(0, 0, 0xC1));
        Assert.Null(fdc.GetDisk(0)!.FindSector(0, 0, 0x41));   // the old numbering is gone
    }

    // ── Read Track ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadTrack_ReturnsEverySectorInPhysicalOrder()
    {
        // Read Track ignores the sector number entirely, which is how a driver
        // reads a track whose numbering it does not know. It cannot be built as
        // a loop over Read Data.
        var fdc = Fdc();

        Command(fdc, 0x42, 0x00, 0, 0, 0x00, 2, 0x49, 0x2A, 0xFF);

        var data = new byte[9 * DskBuilder.SectorSize];
        for (int i = 0; i < data.Length; i++) data[i] = fdc.In(DataPort);

        ReadResult(fdc, 7);

        // Each sector is filled with a byte derived from its number, so the
        // order they arrive in is visible in the data.
        for (int sector = 0; sector < 9; sector++)
        {
            byte expected = DskBuilder.FillFor(0, (byte)(0x41 + sector));
            Assert.Equal(expected, data[sector * DskBuilder.SectorSize]);
        }
    }

    // ── Deleted data ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteDeletedData_MarksTheSector()
    {
        var fdc = Fdc();

        Command(fdc, 0x49, 0x00, 0, 0, 0x43, 2, 0x49, 0x2A, 0xFF);
        for (int i = 0; i < DskBuilder.SectorSize; i++) fdc.Out(DataPort, 0x7E);
        ReadResult(fdc, 7);

        Assert.True(fdc.GetDisk(0)!.FindSector(0, 0, 0x43)!.IsDeleted);
    }

    [Fact]
    public void ReadData_FlagsTheControlMarkOnADeletedSector()
    {
        var fdc = Fdc();
        fdc.GetDisk(0)!.FindSector(0, 0, 0x44)!.IsDeleted = true;

        Command(fdc, 0x46, 0x00, 0, 0, 0x44, 2, 0x49, 0x2A, 0xFF);
        for (int i = 0; i < DskBuilder.SectorSize; i++) fdc.In(DataPort);
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0x40, result[2] & 0x40);   // ST2 CM
    }

    [Fact]
    public void TheSkipBitPassesOverTheWrongKindOfSector()
    {
        // With SK set the controller skips a sector of the wrong kind instead of
        // transferring it, so there is no execution phase at all.
        var fdc = Fdc();
        fdc.GetDisk(0)!.FindSector(0, 0, 0x45)!.IsDeleted = true;

        Command(fdc, 0x66, 0x00, 0, 0, 0x45, 2, 0x49, 0x2A, 0xFF);   // Read Data, SK set
        byte[] result = ReadResult(fdc, 7);

        Assert.Equal(0x40, result[2] & 0x40);
    }

    [Fact]
    public void ReadDeletedData_ReadsADeletedSectorCleanly()
    {
        var fdc = Fdc();
        fdc.GetDisk(0)!.FindSector(0, 0, 0x46)!.IsDeleted = true;

        Command(fdc, 0x4C, 0x00, 0, 0, 0x46, 2, 0x49, 0x2A, 0xFF);
        for (int i = 0; i < DskBuilder.SectorSize; i++) fdc.In(DataPort);
        byte[] result = ReadResult(fdc, 7);

        // The kind matches what was asked for, so no control mark.
        Assert.Equal(0x00, result[2] & 0x40);
    }

    // ── Scan ─────────────────────────────────────────────────────────────────

    private static byte[] RunScan(Upd765a fdc, byte opcode, byte[] supplied)
    {
        Command(fdc, opcode, 0x00, 0, 0, 0x41, 2, 0x49, 0x2A, 0xFF);
        for (int i = 0; i < DskBuilder.SectorSize; i++) fdc.Out(DataPort, supplied[i % supplied.Length]);
        return ReadResult(fdc, 7);
    }

    [Fact]
    public void ScanEqual_ReportsAHitWhenTheDataMatches()
    {
        var fdc = Fdc();
        byte onDisk = DskBuilder.FillFor(0, 0x41);

        byte[] result = RunScan(fdc, 0x51, [onDisk]);

        Assert.Equal(0x08, result[2] & 0x08);   // ST2 SH: scan hit
    }

    [Fact]
    public void ScanEqual_ReportsNotMetWhenTheDataDiffers()
    {
        var fdc = Fdc();
        byte onDisk = DskBuilder.FillFor(0, 0x41);

        byte[] result = RunScan(fdc, 0x51, [(byte)(onDisk ^ 0xFF)]);

        Assert.Equal(0x04, result[2] & 0x04);   // ST2 SN: not satisfied
    }

    [Fact]
    public void ScanLowOrEqual_MatchesWhenTheDiskIsLower()
    {
        var fdc = Fdc();
        byte onDisk = DskBuilder.FillFor(0, 0x41);

        byte[] result = RunScan(fdc, 0x59, [(byte)Math.Min(0xFE, onDisk + 1)]);

        Assert.Equal(0x08, result[2] & 0x08);
    }

    [Fact]
    public void ScanHighOrEqual_MatchesWhenTheDiskIsHigher()
    {
        var fdc = Fdc();
        byte onDisk = DskBuilder.FillFor(0, 0x41);

        byte[] result = RunScan(fdc, 0x5D, [(byte)Math.Max(0, onDisk - 1)]);

        Assert.Equal(0x08, result[2] & 0x08);
    }

    [Fact]
    public void ScanTreats0xFFAsAWildcard()
    {
        var fdc = Fdc();

        byte[] result = RunScan(fdc, 0x51, [0xFF]);

        Assert.Equal(0x08, result[2] & 0x08);
    }

    [Fact]
    public void Reset_ReturnsToTheCommandPhase()
    {
        var fdc = Fdc();

        Command(fdc, 0x0A, 0x00);   // leaves a result phase pending
        Assert.True((fdc.In(StatusPort) & Dio) != 0);

        fdc.Reset();

        Assert.Equal(Rqm, (byte)(fdc.In(StatusPort) & (Rqm | Dio | Exm | Cb)));
    }
}
