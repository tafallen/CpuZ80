using CpuZ80.Core;

namespace Machines.ZxSpectrumPlus3;

/// <summary>
/// NEC uPD765A floppy disk controller, as fitted to the ZX Spectrum +3.
/// </summary>
/// <remarks>
/// A three-phase state machine — command, execution, result — not a register
/// file. The CPU polls the Main Status Register between every byte, and +3DOS
/// trusts it completely: transferring a byte at the wrong moment hangs the
/// driver with no error message, which is a far more likely failure here than
/// getting a status bit wrong.
///
/// See docs/upd765a-fdc.md.
/// </remarks>
public sealed class Upd765a : IPortBus
{
    // ── Main Status Register bits ────────────────────────────────────────────
    private const byte MsrRqm = 0x80;   // ready to transfer a byte
    private const byte MsrDio = 0x40;   // set: FDC -> CPU
    private const byte MsrExm = 0x20;   // execution phase in progress
    private const byte MsrCb  = 0x10;   // controller busy

    // ── ST0 ──────────────────────────────────────────────────────────────────
    private const byte St0Abnormal = 0x40;   // IC = 01
    private const byte St0Invalid  = 0x80;   // IC = 10
    private const byte St0SeekEnd  = 0x20;
    private const byte St0NotReady = 0x08;
    private const byte St0Head     = 0x04;

    // ── ST1 ──────────────────────────────────────────────────────────────────
    private const byte St1EndOfCylinder = 0x80;
    private const byte St1NoData        = 0x04;
    private const byte St1NotWritable   = 0x02;
    private const byte St1MissingAddr   = 0x01;

    // ── ST2 ──────────────────────────────────────────────────────────────────
    private const byte St2ControlMark = 0x40;

    // ── ST3, from Sense Drive Status ─────────────────────────────────────────
    private const byte St3WriteProtected = 0x40;
    private const byte St3Ready          = 0x20;
    private const byte St3Track0         = 0x10;
    private const byte St3TwoSided       = 0x08;

    private enum Phase { Command, Execution, Result }

    private const int DriveCount = 4;   // the +3 fits one, but the FDC addresses four

    private Phase _phase = Phase.Command;

    private readonly byte[] _command = new byte[9];
    private int _commandLength;         // opcode plus parameters
    private int _commandReceived;

    private readonly byte[] _result = new byte[7];
    private int _resultLength;
    private int _resultRead;

    private byte[] _executionBuffer = [];
    private int _executionPosition;
    private bool _executionIsWrite;

    private readonly int[] _presentCylinder = new int[DriveCount];
    private readonly bool[] _seekComplete = new bool[DriveCount];
    private byte _pendingSt0;
    private bool _interruptPending;

    private int _currentDrive;
    private int _currentHead;

    /// <summary>Disks by drive. Null means no disk in that drive, which is not the same as no drive.</summary>
    private readonly DiskImage?[] _disks = new DiskImage?[DriveCount];

    /// <summary>Set by the machine from the pager's decoded motor bit.</summary>
    public bool MotorOn { get; set; }

    public void InsertDisk(int drive, DiskImage? disk)
    {
        if (drive < 0 || drive >= DriveCount) throw new ArgumentOutOfRangeException(nameof(drive));
        _disks[drive] = disk;
    }

    public DiskImage? GetDisk(int drive) =>
        drive >= 0 && drive < DriveCount ? _disks[drive] : null;

    public void Reset()
    {
        _phase = Phase.Command;
        _commandLength = 0;
        _commandReceived = 0;
        _resultLength = 0;
        _resultRead = 0;
        _executionBuffer = [];
        _executionPosition = 0;
        _executionIsWrite = false;
        Array.Clear(_presentCylinder);
        Array.Clear(_seekComplete);
        _pendingSt0 = 0;
        _interruptPending = false;
        _currentDrive = 0;
        _currentHead = 0;
    }

    // ── Ports ────────────────────────────────────────────────────────────────

    /// <summary>Main Status Register: A13 set, A12 set, A1 clear (0x2FFD).</summary>
    private static bool IsStatusPort(ushort port) => (port & 0xF002) == 0x2000;

    /// <summary>Data register: A13 set, A12 set, A1 clear (0x3FFD).</summary>
    private static bool IsDataPort(ushort port) => (port & 0xF002) == 0x3000;

    /// <summary>
    /// The Main Status Register. RQM is always set because this implementation
    /// transfers instantly — there is no rotational delay to wait out.
    /// </summary>
    public byte MainStatus
    {
        get
        {
            byte msr = MsrRqm;

            switch (_phase)
            {
                case Phase.Command:
                    // Busy only once the opcode has been accepted and parameters
                    // are still outstanding.
                    if (_commandReceived > 0) msr |= MsrCb;
                    break;

                case Phase.Execution:
                    msr |= MsrCb | MsrExm;
                    if (!_executionIsWrite) msr |= MsrDio;
                    break;

                case Phase.Result:
                    msr |= MsrCb | MsrDio;
                    break;
            }

            for (int d = 0; d < DriveCount; d++)
            {
                if (_seekComplete[d]) msr |= (byte)(1 << d);
            }

            return msr;
        }
    }

    public byte In(ushort port)
    {
        if (IsStatusPort(port)) return MainStatus;
        if (!IsDataPort(port)) return 0xFF;

        if (_phase == Phase.Execution && !_executionIsWrite)
        {
            byte value = _executionPosition < _executionBuffer.Length
                ? _executionBuffer[_executionPosition]
                : (byte)0x00;
            _executionPosition++;

            if (_executionPosition >= _executionBuffer.Length) EnterResultPhase();
            return value;
        }

        if (_phase == Phase.Result)
        {
            byte value = _result[_resultRead++];
            if (_resultRead >= _resultLength)
            {
                _phase = Phase.Command;
                _resultRead = 0;
                _resultLength = 0;
            }
            return value;
        }

        // Reading the data register outside a transfer returns the last state
        // rather than driving the bus.
        return 0xFF;
    }

    public void Out(ushort port, byte value)
    {
        if (!IsDataPort(port)) return;

        if (_phase == Phase.Execution && _executionIsWrite)
        {
            if (_executionPosition < _executionBuffer.Length)
            {
                _executionBuffer[_executionPosition] = value;
            }
            _executionPosition++;

            if (_executionPosition >= _executionBuffer.Length) EnterResultPhase();
            return;
        }

        if (_phase != Phase.Command) return;

        if (_commandReceived == 0)
        {
            _command[0] = value;
            _commandLength = CommandLength(value);
            _commandReceived = 1;

            if (_commandReceived == _commandLength) Execute();
            return;
        }

        _command[_commandReceived++] = value;
        if (_commandReceived == _commandLength) Execute();
    }

    /// <summary>Total bytes in the command phase — the opcode plus its parameters.</summary>
    private static int CommandLength(byte opcode) => (opcode & 0x1F) switch
    {
        0x03 => 3,   // Specify
        0x04 => 2,   // Sense Drive Status
        0x05 => 9,   // Write Data
        0x06 => 9,   // Read Data
        0x07 => 2,   // Recalibrate
        0x08 => 1,   // Sense Interrupt Status
        0x09 => 9,   // Write Deleted Data
        0x0A => 2,   // Read ID
        0x0C => 9,   // Read Deleted Data
        0x0D => 6,   // Format Track
        0x0F => 3,   // Seek
        _    => 1,   // invalid: the opcode alone, then a single result byte
    };

    // ── Command execution ────────────────────────────────────────────────────

    private void Execute()
    {
        byte opcode = _command[0];

        switch (opcode & 0x1F)
        {
            case 0x03: Specify(); break;
            case 0x04: SenseDriveStatus(); break;
            case 0x05:
            case 0x09: WriteData(); break;
            case 0x06:
            case 0x0C: ReadData(); break;
            case 0x07: Recalibrate(); break;
            case 0x08: SenseInterruptStatus(); break;
            case 0x0A: ReadId(); break;
            case 0x0D: FormatTrack(); break;
            case 0x0F: Seek(); break;
            default: InvalidCommand(); break;
        }
    }

    /// <summary>
    /// Specify sets step rates and DMA mode. It has no result phase at all.
    /// </summary>
    /// <remarks>
    /// This is the first command +3DOS issues, and offering it a result byte
    /// desynchronises every command after it — the trap called out in
    /// docs/upd765a-fdc.md §6.
    /// </remarks>
    private void Specify() => EndWithNoResult();

    private void Recalibrate()
    {
        _currentDrive = _command[1] & 0x03;

        // Recalibrate steps the head back to track 0 and completes
        // asynchronously; the driver collects the outcome with Sense Interrupt
        // Status rather than a result phase.
        _presentCylinder[_currentDrive] = 0;
        _seekComplete[_currentDrive] = true;
        _pendingSt0 = (byte)(St0SeekEnd | _currentDrive);
        if (!DriveReady(_currentDrive)) _pendingSt0 |= (byte)(St0Abnormal | St0NotReady);
        _interruptPending = true;

        EndWithNoResult();
    }

    private void Seek()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;
        _presentCylinder[_currentDrive] = _command[2];

        _seekComplete[_currentDrive] = true;
        _pendingSt0 = (byte)(St0SeekEnd | (_currentHead != 0 ? St0Head : 0) | _currentDrive);
        if (!DriveReady(_currentDrive)) _pendingSt0 |= (byte)(St0Abnormal | St0NotReady);
        _interruptPending = true;

        EndWithNoResult();
    }

    private void SenseInterruptStatus()
    {
        if (!_interruptPending)
        {
            // Nothing to report: the invalid-command code, which is how the
            // driver discovers it has drained every pending seek.
            _result[0] = St0Invalid;
            EnterResultPhase(1);
            return;
        }

        _result[0] = _pendingSt0;
        _result[1] = (byte)_presentCylinder[_pendingSt0 & 0x03];

        _seekComplete[_pendingSt0 & 0x03] = false;
        _interruptPending = false;

        EnterResultPhase(2);
    }

    private void SenseDriveStatus()
    {
        int drive = _command[1] & 0x03;
        int head = (_command[1] >> 2) & 1;
        var disk = _disks[drive];

        byte st3 = (byte)(drive | (head != 0 ? St0Head : 0));

        if (DriveReady(drive)) st3 |= St3Ready;
        if (_presentCylinder[drive] == 0) st3 |= St3Track0;
        if (disk is not null)
        {
            if (disk.IsWriteProtected) st3 |= St3WriteProtected;
            if (disk.SideCount > 1) st3 |= St3TwoSided;
        }

        _result[0] = st3;
        EnterResultPhase(1);
    }

    private void ReadId()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

        var track = CurrentTrack();

        if (!DriveReady(_currentDrive))
        {
            SetReadWriteResult(St0Abnormal | St0NotReady, St1MissingAddr, 0, 0, 0, 0, 0);
            return;
        }

        if (track is null || track.IsUnformatted)
        {
            SetReadWriteResult(St0Abnormal, St1MissingAddr, 0,
                (byte)_presentCylinder[_currentDrive], (byte)_currentHead, 0, 0);
            return;
        }

        // A real head reads whichever ID passes next. Rotating through them is
        // what lets a driver enumerate a track, and returning the recorded
        // numbering is how +3DOS tells a system disk from a data disk.
        var sector = track.Sectors[_readIdIndex % track.Sectors.Count];
        _readIdIndex++;

        SetReadWriteResult(0, 0, 0, sector.C, sector.H, sector.R, sector.N);
    }

    private int _readIdIndex;

    private void ReadData()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

        byte c = _command[2];
        byte h = _command[3];
        byte r = _command[4];
        byte n = _command[5];

        if (!DriveReady(_currentDrive))
        {
            SetReadWriteResult(St0Abnormal | St0NotReady, St1MissingAddr, 0, c, h, r, n);
            return;
        }

        var sector = FindSector(r);
        if (sector is null)
        {
            SetReadWriteResult(St0Abnormal, St1NoData, 0, c, h, r, n);
            return;
        }

        _executionBuffer = sector.Data;
        _executionPosition = 0;
        _executionIsWrite = false;
        _phase = Phase.Execution;

        // The result bytes are prepared now and delivered once the transfer
        // drains. A deleted-data sector still transfers, but flags the control
        // mark so the driver can tell.
        PrepareReadWriteResult(
            0,
            sector.St1,
            (byte)(sector.St2 | (sector.IsDeleted ? St2ControlMark : 0)),
            sector.C, sector.H, sector.R, sector.N);
    }

    private void WriteData()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

        byte c = _command[2];
        byte h = _command[3];
        byte r = _command[4];
        byte n = _command[5];

        var disk = _disks[_currentDrive];

        if (!DriveReady(_currentDrive))
        {
            SetReadWriteResult(St0Abnormal | St0NotReady, St1MissingAddr, 0, c, h, r, n);
            return;
        }

        if (disk!.IsWriteProtected)
        {
            SetReadWriteResult(St0Abnormal, St1NotWritable, 0, c, h, r, n);
            return;
        }

        var sector = FindSector(r);
        if (sector is null)
        {
            SetReadWriteResult(St0Abnormal, St1NoData, 0, c, h, r, n);
            return;
        }

        _executionBuffer = sector.Data;
        _executionPosition = 0;
        _executionIsWrite = true;
        _phase = Phase.Execution;

        PrepareReadWriteResult(0, 0, 0, sector.C, sector.H, sector.R, sector.N);
    }

    private void FormatTrack()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

        byte filler = _command[5];
        var track = CurrentTrack();

        if (!DriveReady(_currentDrive))
        {
            SetReadWriteResult(St0Abnormal | St0NotReady, St1MissingAddr, 0, 0, 0, 0, 0);
            return;
        }

        if (_disks[_currentDrive]!.IsWriteProtected)
        {
            SetReadWriteResult(St0Abnormal, St1NotWritable, 0, 0, 0, 0, 0);
            return;
        }

        // Formatting rewrites the sector IDs on a real disk. Reshaping a track
        // in a .DSK image is a bigger change than this story needs, so the
        // existing sectors are filled instead: enough for a disk to be
        // "formatted" and then written, but it cannot change the geometry.
        if (track is not null)
        {
            foreach (var sector in track.Sectors) Array.Fill(sector.Data, filler);
        }

        SetReadWriteResult(0, 0, 0, (byte)_presentCylinder[_currentDrive], (byte)_currentHead, 0, _command[2]);
    }

    private void InvalidCommand()
    {
        // Silently ignoring an unknown opcode hangs the driver, which polls for
        // a result that never arrives.
        _result[0] = St0Invalid;
        EnterResultPhase(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A drive is ready only with a disk in it and the motor running.</summary>
    private bool DriveReady(int drive) => _disks[drive] is not null && MotorOn;

    private DiskImage.Track? CurrentTrack() =>
        _disks[_currentDrive]?.GetTrack(_presentCylinder[_currentDrive], _currentHead);

    private DiskImage.Sector? FindSector(byte r) =>
        _disks[_currentDrive]?.FindSector(_presentCylinder[_currentDrive], _currentHead, r);

    private void EndWithNoResult()
    {
        _phase = Phase.Command;
        _commandReceived = 0;
        _commandLength = 0;
    }

    private void PrepareReadWriteResult(byte st0, byte st1, byte st2, byte c, byte h, byte r, byte n)
    {
        _result[0] = (byte)(st0 | (_currentHead != 0 ? St0Head : 0) | _currentDrive);
        _result[1] = st1;
        _result[2] = st2;
        _result[3] = c;
        _result[4] = h;
        _result[5] = r;
        _result[6] = n;
        _resultLength = 7;
    }

    private void SetReadWriteResult(int st0, int st1, int st2, byte c, byte h, byte r, byte n)
    {
        PrepareReadWriteResult((byte)st0, (byte)st1, (byte)st2, c, h, r, n);
        EnterResultPhase(7);
    }

    private void EnterResultPhase(int length)
    {
        _resultLength = length;
        _resultRead = 0;
        _commandReceived = 0;
        _commandLength = 0;
        _phase = Phase.Result;
    }

    /// <summary>Ends an execution phase, moving to the result bytes prepared when it began.</summary>
    private void EnterResultPhase() => EnterResultPhase(_resultLength);
}
