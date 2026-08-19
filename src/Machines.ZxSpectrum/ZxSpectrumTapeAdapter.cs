using Machines.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// ITapeDevice implementation for ZX Spectrum pulse-width modulation (PWM) encoding.
/// Supports standard .TAP files.
/// </summary>
public sealed class ZxSpectrumTapeAdapter : ITapeDevice
{
    private enum TapeState
    {
        Idle,
        Pilot,
        Sync1,
        Sync2,
        Data,
        Pause
    }

    private readonly List<byte[]> _blocks = new();
    private int      _currentBlockIdx = -1;
    private int      _currentByteIdx;
    private int      _currentBitIdx;
    
    private TapeState _state = TapeState.Idle;
    private int       _pulseCount;
    private ulong     _lastTransitionTState;
    private int       _currentPulseDuration;
    private bool      _currentSignal;

    private const int PilotDuration = 2168;
    private const int Sync1Duration = 667;
    private const int Sync2Duration = 735;
    private const int Bit0Duration  = 855;
    private const int Bit1Duration  = 1710;
    private const int PauseDuration = 3500000; // 1 second

    public void Load(Stream data)
    {
        _blocks.Clear();
        _currentBlockIdx = -1;
        _state = TapeState.Idle;
        _initialized = false;

        while (data.Position < data.Length)
        {
            int lo = data.ReadByte();
            int hi = data.ReadByte();
            if (lo == -1 || hi == -1) break; // EOF

            ushort len = (ushort)(lo | (hi << 8));
            if (len == 0) continue;

            if (data.Position + len > data.Length)
            {
                throw new InvalidDataException($".TAP block length {len} exceeds remaining stream size.");
            }
            
            byte[] block = new byte[len];
            int read = data.Read(block, 0, len);
            if (read != len)
            {
                throw new InvalidDataException($".TAP block read failure: expected {len} bytes, got {read}.");
            }

            _blocks.Add(block);
        }

        if (_blocks.Count > 0)
        {
            _currentBlockIdx = 0;
            StartBlock();
        }
    }

    private void StartBlock()
    {
        if (_currentBlockIdx < 0 || _currentBlockIdx >= _blocks.Count)
        {
            _state = TapeState.Idle;
            return;
        }

        byte flag = _blocks[_currentBlockIdx][0];
        _pulseCount = (flag < 128) ? 8063 : 3223; // Header vs Data pilot
        _state = TapeState.Pilot;
        _currentPulseDuration = PilotDuration;
        _currentSignal = false;
        _lastTransitionTState = 0; // Reset will be handled on first ReadBit
    }

    private bool      _initialized;

    public bool ReadBit(ulong currentTState)
    {
        if (_state == TapeState.Idle) return true;

        if (!_initialized)
        {
            _lastTransitionTState = currentTState;
            _initialized = true;
        }

        if (currentTState < _lastTransitionTState) return _currentSignal;

        while (currentTState - _lastTransitionTState >= (ulong)_currentPulseDuration && _state != TapeState.Idle)
        {
            _currentSignal = !_currentSignal;
            _lastTransitionTState += (ulong)_currentPulseDuration;
            
            _pulseCount--;
            if (_pulseCount <= 0)
            {
                AdvanceState();
                // If the new state has a 0 duration or we transitioned to Idle, stop.
                if (_state == TapeState.Idle) break;
            }
        }

        return _currentSignal;
    }

    private void AdvanceState()
    {
        switch (_state)
        {
            case TapeState.Pilot:
                _state = TapeState.Sync1;
                _currentPulseDuration = Sync1Duration;
                _pulseCount = 1;
                break;

            case TapeState.Sync1:
                _state = TapeState.Sync2;
                _currentPulseDuration = Sync2Duration;
                _pulseCount = 1;
                break;

            case TapeState.Sync2:
                _state = TapeState.Data;
                _currentByteIdx = 0;
                _currentBitIdx = 7;
                SetNextBitPulses();
                break;

            case TapeState.Data:
                _currentBitIdx--;
                if (_currentBitIdx < 0)
                {
                    _currentBitIdx = 7;
                    _currentByteIdx++;
                }

                if (_currentByteIdx >= _blocks[_currentBlockIdx].Length)
                {
                    _state = TapeState.Pause;
                    _currentPulseDuration = PauseDuration;
                    _pulseCount = 1;
                }
                else
                {
                    SetNextBitPulses();
                }
                break;

            case TapeState.Pause:
                _currentBlockIdx++;
                if (_currentBlockIdx < _blocks.Count)
                {
                    StartBlock();
                }
                else
                {
                    _state = TapeState.Idle;
                }
                break;
        }
    }

    private void SetNextBitPulses()
    {
        byte b = _blocks[_currentBlockIdx][_currentByteIdx];
        bool isOne = (b & (1 << _currentBitIdx)) != 0;
        _currentPulseDuration = isOne ? Bit1Duration : Bit0Duration;
        _pulseCount = 2; // Each bit is two pulses
    }

    // ── Recording ────────────────────────────────────────────────────────────
    //
    // Saving is loading in reverse: MIC edges are timed, each gap between edges
    // classified against the same pulse widths playback uses, and the result
    // reassembled into .TAP blocks.

    private enum RecordState { Idle, Pilot, Sync, Data }

    private readonly List<byte[]> _recordedBlocks = [];
    private readonly List<byte> _blockBytes = [];

    private RecordState _recordState = RecordState.Idle;
    private bool _recordStarted;
    private bool _lastMic;
    private ulong _lastEdge;
    private int _pilotPulses;
    private int _pendingPulse;      // first half of a bit's pulse pair, or 0
    private int _currentByte;
    private int _bitsInByte;

    /// <summary>Pilot pulses needed before a sync pair is believed.</summary>
    /// <remarks>
    /// The ROM emits thousands, but requiring anything near that would reject a
    /// short leader. Enough to not mistake data for a leader is the useful test.
    /// </remarks>
    private const int MinimumPilotPulses = 16;

    /// <summary>A gap this long with no edge ends the block.</summary>
    private const int SilenceTStates = 5_000;

    /// <summary>Blocks decoded from what the machine has saved so far.</summary>
    public IReadOnlyList<byte[]> RecordedBlocks => _recordedBlocks;

    /// <summary>Untimed MIC writes cannot be decoded, so they are ignored.</summary>
    /// <remarks>
    /// Tape encoding is entirely pulse widths. Accepting a level with no
    /// timestamp and assuming a duration would produce plausible but wrong data,
    /// which is worse than recording nothing.
    /// </remarks>
    public void WriteBit(bool bit) { }

    public void WriteBit(bool bit, ulong currentTState)
    {
        if (!_recordStarted)
        {
            _recordStarted = true;
            _lastMic = bit;
            _lastEdge = currentTState;
            return;
        }

        if (bit == _lastMic)
        {
            // No edge. A long enough hold is the gap between blocks.
            if (currentTState - _lastEdge > SilenceTStates) EndBlock();
            return;
        }

        ulong duration = currentTState - _lastEdge;
        _lastMic = bit;
        _lastEdge = currentTState;

        ClassifyPulse((int)Math.Min(duration, int.MaxValue));
    }

    private static bool Near(int duration, int reference, double tolerance = 0.4) =>
        duration >= reference * (1 - tolerance) && duration <= reference * (1 + tolerance);

    /// <summary>
    /// Shortest pulse counted as pilot rather than a 1 bit.
    /// </summary>
    /// <remarks>
    /// A 1 bit is 1710 T-states and a pilot pulse 2168 — close enough that a
    /// percentage tolerance around the pilot reaches down over the bit. With a
    /// 20% window the boundary sat 24 T-states above a 1 bit, so the smallest
    /// jitter turned a data bit into a new pilot tone and silently truncated the
    /// block. The midpoint between the two is the only stable place to split
    /// them.
    /// </remarks>
    private const int PilotThreshold = (Bit1Duration + PilotDuration) / 2;

    private void ClassifyPulse(int duration)
    {
        // A pilot pulse while data is being read means the next block has begun
        // without a silence between them.
        if (duration >= PilotThreshold && duration <= PilotDuration * 3 / 2)
        {
            if (_recordState == RecordState.Data) EndBlock();
            _pilotPulses++;
            _recordState = RecordState.Pilot;
            return;
        }

        switch (_recordState)
        {
            case RecordState.Pilot when _pilotPulses >= MinimumPilotPulses && Near(duration, Sync1Duration, 0.3):
                _recordState = RecordState.Sync;
                return;

            case RecordState.Sync when Near(duration, Sync2Duration, 0.3):
                _recordState = RecordState.Data;
                _pendingPulse = 0;
                _currentByte = 0;
                _bitsInByte = 0;
                return;

            case RecordState.Data:
                RecordBitPulse(duration);
                return;
        }
    }

    private void RecordBitPulse(int duration)
    {
        // Every data bit is two pulses of the same width, so they are paired
        // before being classified — judging a bit from one pulse would double
        // the bit rate and produce twice as much nonsense.
        if (_pendingPulse == 0)
        {
            _pendingPulse = duration;
            return;
        }

        int average = (_pendingPulse + duration) / 2;
        _pendingPulse = 0;

        // Midway between the two widths, so a pulse is read as whichever it is
        // closer to rather than being rejected for being slightly off.
        bool one = average > (Bit0Duration + Bit1Duration) / 2;

        _currentByte = (_currentByte << 1) | (one ? 1 : 0);
        _bitsInByte++;

        if (_bitsInByte == 8)
        {
            _blockBytes.Add((byte)_currentByte);
            _currentByte = 0;
            _bitsInByte = 0;
        }
    }

    private void EndBlock()
    {
        if (_blockBytes.Count > 0) _recordedBlocks.Add([.. _blockBytes]);

        _blockBytes.Clear();
        _recordState = RecordState.Idle;
        _pilotPulses = 0;
        _pendingPulse = 0;
        _currentByte = 0;
        _bitsInByte = 0;
    }

    /// <summary>
    /// Closes any block still being recorded. Called by <see cref="Save"/>, and
    /// needed because a machine that simply stops saving produces no final edge
    /// to end the block with.
    /// </summary>
    public void FinishRecording() => EndBlock();

    /// <summary>Writes everything recorded so far as a .TAP file.</summary>
    public void Save(Stream destination)
    {
        FinishRecording();

        foreach (byte[] block in _recordedBlocks)
        {
            destination.WriteByte((byte)(block.Length & 0xFF));
            destination.WriteByte((byte)(block.Length >> 8));
            destination.Write(block, 0, block.Length);
        }
    }

    /// <summary>Discards the recording, leaving any loaded tape alone.</summary>
    public void ClearRecording()
    {
        _recordedBlocks.Clear();
        EndBlock();
        _recordStarted = false;
    }
}
