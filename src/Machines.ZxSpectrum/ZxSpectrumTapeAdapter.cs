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

    public void WriteBit(bool bit) { }
}
