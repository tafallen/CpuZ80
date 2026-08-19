using CpuZ80.Core;

namespace Machines.ZxSpectrumPlus3;

/// <summary>
/// NEC uPD765A floppy disk controller, as fitted to the ZX Spectrum +3 and the
/// Amstrad CPC 6128.
/// </summary>
/// <remarks>
/// A three-phase state machine — command, execution, result — not a register
/// file. The CPU polls the Main Status Register between every byte, and +3DOS
/// trusts it completely: transferring a byte at the wrong moment hangs the
/// driver with no error message.
///
/// Timing is optional. With no <see cref="Clock"/> the controller completes
/// everything instantly, which is what most software needs and what the tests
/// use. Given a clock it models seek times and the disk's data rate, which is
/// what loaders that measure the controller rely on.
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
    private const byte St2ControlMark   = 0x40;
    private const byte St2ScanNotMet    = 0x04;
    private const byte St2ScanHit       = 0x08;

    // ── ST3, from Sense Drive Status ─────────────────────────────────────────
    private const byte St3WriteProtected = 0x40;
    private const byte St3Ready          = 0x20;
    private const byte St3Track0         = 0x10;
    private const byte St3TwoSided       = 0x08;

    private enum Phase { Command, Execution, Result }

    /// <summary>What the current execution phase is doing with its buffer.</summary>
    private enum Transfer { Read, Write, Scan, Format }

    private const int DriveCount = 4;   // the +3 fits one, but the FDC addresses four

    private Phase _phase = Phase.Command;

    private readonly byte[] _command = new byte[9];
    private int _commandLength;
    private int _commandReceived;

    private readonly byte[] _result = new byte[7];
    private int _resultLength;
    private int _resultRead;

    private byte[] _executionBuffer = [];
    private int _executionPosition;
    private Transfer _transfer;

    private readonly int[] _presentCylinder = new int[DriveCount];
    private readonly bool[] _seekComplete = new bool[DriveCount];
    private byte _pendingSt0;
    private bool _interruptPending;

    private int _currentDrive;
    private int _currentHead;
    private int _readIdIndex;

    private readonly DiskImage?[] _disks = new DiskImage?[DriveCount];

    /// <summary>Set by the machine from the pager's decoded motor bit.</summary>
    public bool MotorOn { get; set; }

    // ── Timing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The host's cycle counter. Null means everything completes instantly.
    /// </summary>
    /// <remarks>
    /// Optional because instant completion is correct enough for +3DOS and
    /// AMSDOS, and because a controller that suddenly takes time would break
    /// every caller that polls without advancing a clock. Loaders that measure
    /// the controller — which is most disk copy protection — need the real
    /// thing.
    /// </remarks>
    public Func<ulong>? Clock { get; set; }

    /// <summary>Host clock rate, used to turn the datasheet's microseconds into T-states.</summary>
    public int ClockHz { get; set; } = 4_000_000;

    /// <summary>Microseconds to step the head one track. Set by Specify.</summary>
    public int StepRateMicroseconds { get; private set; } = 6_000;

    /// <summary>
    /// Microseconds per byte at the disk's data rate: 250 kbit/s MFM is 32us.
    /// </summary>
    public int ByteMicroseconds { get; set; } = 32;

    private ulong _readyAt;
    private readonly ulong[] _seekDoneAt = new ulong[DriveCount];

    private bool TimingEnabled => Clock is not null;
    private ulong Now => Clock?.Invoke() ?? 0;

    private ulong Microseconds(int us) => (ulong)((long)us * ClockHz / 1_000_000);

    private void DelayBy(int microseconds)
    {
        if (TimingEnabled) _readyAt = Now + Microseconds(microseconds);
    }

    private bool WaitingForData => TimingEnabled && Now < _readyAt;

    private bool Seeking(int drive) => TimingEnabled && Now < _seekDoneAt[drive];

    // ── Disks ────────────────────────────────────────────────────────────────

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
        _transfer = Transfer.Read;
        Array.Clear(_presentCylinder);
        Array.Clear(_seekComplete);
        Array.Clear(_seekDoneAt);
        _pendingSt0 = 0;
        _interruptPending = false;
        _currentDrive = 0;
        _currentHead = 0;
        _readIdIndex = 0;
        _readyAt = 0;
        StepRateMicroseconds = 6_000;
    }

    // ── Ports ────────────────────────────────────────────────────────────────

    /// <summary>Main Status Register: A13 set, A12 set, A1 clear (0x2FFD).</summary>
    private static bool IsStatusPort(ushort port) => (port & 0xF002) == 0x2000;

    /// <summary>Data register: A13 set, A12 set, A1 clear (0x3FFD).</summary>
    private static bool IsDataPort(ushort port) => (port & 0xF002) == 0x3000;

    /// <summary>The Main Status Register.</summary>
    public byte MainStatus
    {
        get
        {
            byte msr = 0;

            // RQM drops while the disk is between bytes. Without a clock there
            // is no rotational delay to wait out, so it is always ready.
            if (!WaitingForData) msr |= MsrRqm;

            switch (_phase)
            {
                case Phase.Command:
                    if (_commandReceived > 0) msr |= MsrCb;
                    break;

                case Phase.Execution:
                    msr |= MsrCb | MsrExm;
                    if (_transfer is Transfer.Read) msr |= MsrDio;
                    break;

                case Phase.Result:
                    msr |= MsrCb | MsrDio;
                    break;
            }

            for (int d = 0; d < DriveCount; d++)
            {
                if (_seekComplete[d] || Seeking(d)) msr |= (byte)(1 << d);
            }

            return msr;
        }
    }

    public byte In(ushort port)
    {
        if (IsStatusPort(port)) return MainStatus;
        if (!IsDataPort(port)) return 0xFF;

        if (_phase == Phase.Execution && _transfer == Transfer.Read)
        {
            if (WaitingForData) return 0xFF;

            byte value = _executionPosition < _executionBuffer.Length
                ? _executionBuffer[_executionPosition]
                : (byte)0x00;
            _executionPosition++;
            DelayBy(ByteMicroseconds);

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

        return 0xFF;
    }

    public void Out(ushort port, byte value)
    {
        if (!IsDataPort(port)) return;

        if (_phase == Phase.Execution && _transfer != Transfer.Read)
        {
            if (WaitingForData) return;

            AcceptExecutionByte(value);
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

    private void AcceptExecutionByte(byte value)
    {
        switch (_transfer)
        {
            case Transfer.Write:
                if (_executionPosition < _executionBuffer.Length)
                {
                    _executionBuffer[_executionPosition] = value;
                }
                break;

            case Transfer.Scan:
                CompareScanByte(value);
                break;

            case Transfer.Format:
                if (_executionPosition < _executionBuffer.Length)
                {
                    _executionBuffer[_executionPosition] = value;
                }
                break;
        }

        _executionPosition++;
        DelayBy(ByteMicroseconds);

        if (_executionPosition >= _executionBuffer.Length)
        {
            if (_transfer == Transfer.Format) LayDownTrack();
            EnterResultPhase();
        }
    }

    /// <summary>Total bytes in the command phase — the opcode plus its parameters.</summary>
    private static int CommandLength(byte opcode) => (opcode & 0x1F) switch
    {
        0x02 => 9,   // Read Track
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
        0x11 => 9,   // Scan Equal
        0x19 => 9,   // Scan Low or Equal
        0x1D => 9,   // Scan High or Equal
        _    => 1,   // invalid: the opcode alone, then a single result byte
    };

    // ── Command execution ────────────────────────────────────────────────────

    private void Execute()
    {
        byte opcode = _command[0];

        switch (opcode & 0x1F)
        {
            case 0x02: ReadTrack(); break;
            case 0x03: Specify(); break;
            case 0x04: SenseDriveStatus(); break;
            case 0x05: WriteData(deleted: false); break;
            case 0x09: WriteData(deleted: true); break;
            case 0x06: ReadData(deleted: false); break;
            case 0x0C: ReadData(deleted: true); break;
            case 0x07: Recalibrate(); break;
            case 0x08: SenseInterruptStatus(); break;
            case 0x0A: ReadId(); break;
            case 0x0D: FormatTrack(); break;
            case 0x0F: Seek(); break;
            case 0x11:
            case 0x19:
            case 0x1D: Scan(opcode & 0x1F); break;
            default: InvalidCommand(); break;
        }
    }

    /// <summary>
    /// Specify sets step rates and DMA mode. It has no result phase at all.
    /// </summary>
    /// <remarks>
    /// This is the first command +3DOS issues, and offering it a result byte
    /// desynchronises every command after it.
    /// </remarks>
    private void Specify()
    {
        // The step rate is the top nibble, counting down from 16 in units of a
        // millisecond at 250 kbit/s.
        int srt = (_command[1] >> 4) & 0x0F;
        StepRateMicroseconds = (16 - srt) * 1_000;

        EndWithNoResult();
    }

    private void Recalibrate()
    {
        _currentDrive = _command[1] & 0x03;

        int distance = _presentCylinder[_currentDrive];
        _presentCylinder[_currentDrive] = 0;
        StartSeek(distance);

        EndWithNoResult();
    }

    private void Seek()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

        int distance = Math.Abs(_command[2] - _presentCylinder[_currentDrive]);
        _presentCylinder[_currentDrive] = _command[2];
        StartSeek(distance);

        EndWithNoResult();
    }

    /// <summary>
    /// Marks a seek as under way. Seeks complete asynchronously, and the driver
    /// collects the outcome with Sense Interrupt Status rather than a result
    /// phase.
    /// </summary>
    private void StartSeek(int tracksMoved)
    {
        _seekComplete[_currentDrive] = true;

        if (TimingEnabled)
        {
            _seekDoneAt[_currentDrive] = Now + Microseconds(Math.Max(1, tracksMoved) * StepRateMicroseconds);
        }

        _pendingSt0 = (byte)(St0SeekEnd | (_currentHead != 0 ? St0Head : 0) | _currentDrive);
        if (!DriveReady(_currentDrive)) _pendingSt0 |= (byte)(St0Abnormal | St0NotReady);
        _interruptPending = true;
    }

    private void SenseInterruptStatus()
    {
        int drive = _pendingSt0 & 0x03;

        // A seek still in progress has nothing to report yet.
        if (!_interruptPending || Seeking(drive))
        {
            _result[0] = St0Invalid;
            EnterResultPhase(1);
            return;
        }

        _result[0] = _pendingSt0;
        _result[1] = (byte)_presentCylinder[drive];

        _seekComplete[drive] = false;
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

    // ── Read ─────────────────────────────────────────────────────────────────

    private void ReadData(bool deleted)
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

        // Read Data wants normal sectors and Read Deleted Data wants deleted
        // ones. Meeting the wrong kind sets the control mark, and with the skip
        // bit set the controller passes over it rather than transferring it.
        bool wrongKind = sector.IsDeleted != deleted;
        bool skip = (_command[0] & 0x20) != 0;

        if (wrongKind && skip)
        {
            SetReadWriteResult(St0Abnormal, 0, St2ControlMark, sector.C, sector.H, sector.R, sector.N);
            return;
        }

        BeginTransfer(sector.Data, Transfer.Read);

        // The control mark reports that the data mark found did not match the
        // command, not simply that the sector is deleted. Passing the sector's
        // own ST2 straight through would flag every deleted sector even when
        // Read Deleted Data asked for exactly that.
        byte st2 = (byte)((sector.St2 & ~St2ControlMark) | (wrongKind ? St2ControlMark : 0));

        PrepareReadWriteResult(0, sector.St1, st2, sector.C, sector.H, sector.R, sector.N);
    }

    /// <summary>
    /// Read Track hands over every sector on the track in the order they are
    /// physically laid down, ignoring the sector number entirely.
    /// </summary>
    /// <remarks>
    /// This is how a driver reads a track whose numbering it does not know, and
    /// why it cannot be implemented as a loop over Read Data.
    /// </remarks>
    private void ReadTrack()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

        if (!DriveReady(_currentDrive))
        {
            SetReadWriteResult(St0Abnormal | St0NotReady, St1MissingAddr, 0,
                _command[2], _command[3], _command[4], _command[5]);
            return;
        }

        var track = CurrentTrack();
        if (track is null || track.IsUnformatted)
        {
            SetReadWriteResult(St0Abnormal, St1MissingAddr, 0,
                _command[2], _command[3], _command[4], _command[5]);
            return;
        }

        int total = 0;
        foreach (var sector in track.Sectors) total += sector.Data.Length;

        byte[] whole = new byte[total];
        int offset = 0;
        foreach (var sector in track.Sectors)
        {
            Array.Copy(sector.Data, 0, whole, offset, sector.Data.Length);
            offset += sector.Data.Length;
        }

        BeginTransfer(whole, Transfer.Read);

        var last = track.Sectors[^1];
        PrepareReadWriteResult(0, 0, 0, last.C, last.H, last.R, last.N);
    }

    // ── Write ────────────────────────────────────────────────────────────────

    private void WriteData(bool deleted)
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

        if (_disks[_currentDrive]!.IsWriteProtected)
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

        // Writing deleted data is what puts the control mark on a sector; it is
        // a property of the sector afterwards, not of this command.
        sector.IsDeleted = deleted;

        BeginTransfer(sector.Data, Transfer.Write);

        PrepareReadWriteResult(0, 0, deleted ? St2ControlMark : (byte)0,
            sector.C, sector.H, sector.R, sector.N);
    }

    // ── Scan ─────────────────────────────────────────────────────────────────

    private int _scanCommand;
    private bool _scanSatisfied;
    private DiskImage.Sector? _scanSector;

    /// <summary>
    /// Scan compares data the CPU supplies against what is on the disk, rather
    /// than transferring anything either way.
    /// </summary>
    private void Scan(int command)
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

        _scanCommand = command;
        _scanSector = sector;
        _scanSatisfied = true;      // stays true until a byte fails the test
        _executionPosition = 0;
        _executionBuffer = new byte[sector.Data.Length];
        _transfer = Transfer.Scan;
        _phase = Phase.Execution;
        DelayBy(ByteMicroseconds);

        PrepareReadWriteResult(0, 0, 0, sector.C, sector.H, sector.R, sector.N);
    }

    private void CompareScanByte(byte fromCpu)
    {
        if (_scanSector is null || _executionPosition >= _scanSector.Data.Length) return;

        byte onDisk = _scanSector.Data[_executionPosition];

        // 0xFF from either side is a wildcard and always matches.
        if (fromCpu == 0xFF || onDisk == 0xFF) return;

        bool ok = _scanCommand switch
        {
            0x11 => onDisk == fromCpu,   // Scan Equal
            0x19 => onDisk <= fromCpu,   // Scan Low or Equal
            _    => onDisk >= fromCpu,   // Scan High or Equal
        };

        if (!ok) _scanSatisfied = false;
    }

    // ── Format ───────────────────────────────────────────────────────────────

    private void FormatTrack()
    {
        _currentDrive = _command[1] & 0x03;
        _currentHead = (_command[1] >> 2) & 1;

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

        // The CPU supplies C, H, R and N for every sector it wants laid down, so
        // formatting has an execution phase like a transfer does. Without it the
        // controller can only refill sectors that already exist and can never
        // change a track's geometry.
        int sectorsPerTrack = _command[3];
        if (sectorsPerTrack == 0)
        {
            SetReadWriteResult(St0Abnormal, St1MissingAddr, 0, 0, 0, 0, _command[2]);
            return;
        }

        _executionBuffer = new byte[sectorsPerTrack * 4];
        _executionPosition = 0;
        _transfer = Transfer.Format;
        _phase = Phase.Execution;
        DelayBy(ByteMicroseconds);

        PrepareReadWriteResult(0, 0, 0, 0, 0, 0, _command[2]);
    }

    private void LayDownTrack()
    {
        byte n = _command[2];
        byte filler = _command[5];
        int size = 128 << Math.Min(n, (byte)7);

        var sectors = new List<DiskImage.Sector>(_executionBuffer.Length / 4);

        for (int i = 0; i + 3 < _executionBuffer.Length; i += 4)
        {
            byte[] data = new byte[size];
            Array.Fill(data, filler);

            sectors.Add(new DiskImage.Sector
            {
                C = _executionBuffer[i + 0],
                H = _executionBuffer[i + 1],
                R = _executionBuffer[i + 2],
                N = _executionBuffer[i + 3],
                Data = data,
            });
        }

        _disks[_currentDrive]?.FormatTrack(_presentCylinder[_currentDrive], _currentHead, sectors);
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

    private void BeginTransfer(byte[] buffer, Transfer transfer)
    {
        _executionBuffer = buffer;
        _executionPosition = 0;
        _transfer = transfer;
        _phase = Phase.Execution;
        DelayBy(ByteMicroseconds);
    }

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
    private void EnterResultPhase()
    {
        // A scan reports its verdict in ST2 rather than by transferring data.
        if (_transfer == Transfer.Scan)
        {
            _result[2] |= _scanSatisfied ? St2ScanHit : St2ScanNotMet;
            _scanSector = null;
        }

        EnterResultPhase(_resultLength);
    }
}
