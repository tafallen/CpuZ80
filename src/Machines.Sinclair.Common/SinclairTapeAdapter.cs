using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// Sinclair pulse-count tape encoding, as used by the ZX80 (<c>.o</c>) and
/// ZX81 (<c>.p</c>).
/// </summary>
/// <remarks>
/// Each bit is a burst of pulses followed by a silence: four pulses for a 0 and
/// nine for a 1. A pulse is 150 microseconds high then 150 low, and the gap
/// between bits is 1300 microseconds. Bytes are MSB first.
///
/// Playback is timed from the CPU's cycle counter rather than advancing one
/// level per call. The earlier version returned the next level on every
/// <see cref="ReadBit"/>, which made the pulse rate depend on how often the ULA
/// happened to be polled instead of on elapsed time — the encoding is entirely
/// about durations, so that could not load a real file.
///
/// Recording is the same encoding in reverse: MIC transitions are timed, pulses
/// counted between gaps, and the count turned back into bits.
/// </remarks>
public sealed class SinclairTapeAdapter : ITapeDevice
{
    /// <summary>ZX80 and ZX81 both run at 3.25 MHz.</summary>
    public const int ClockHz = 3_250_000;

    private const int HalfPulseTStates = (int)(150e-6 * ClockHz);   // 487
    private const int BitGapTStates = (int)(1300e-6 * ClockHz);     // 4225

    private const int PulsesForZero = 4;
    private const int PulsesForOne = 9;

    /// <summary>A level and how long it lasts.</summary>
    private readonly record struct Pulse(bool Level, int TStates);

    private readonly List<Pulse> _pulses = [];
    private int _index;
    private ulong _pulseStart;
    private bool _started;

    /// <summary>True once the whole tape has played out.</summary>
    public bool AtEnd => _index >= _pulses.Count;

    /// <summary>Total playing time of the loaded tape, in T-states.</summary>
    public ulong LengthInTStates
    {
        get
        {
            ulong total = 0;
            foreach (var pulse in _pulses) total += (ulong)pulse.TStates;
            return total;
        }
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    public void Load(Stream data)
    {
        _pulses.Clear();
        _index = 0;
        _started = false;

        int b;
        while ((b = data.ReadByte()) != -1) EncodeByte((byte)b);
    }

    private void EncodeByte(byte value)
    {
        for (int bit = 7; bit >= 0; bit--) EncodeBit(((value >> bit) & 1) != 0);
    }

    private void EncodeBit(bool one)
    {
        int pulses = one ? PulsesForOne : PulsesForZero;

        for (int p = 0; p < pulses; p++)
        {
            _pulses.Add(new Pulse(true, HalfPulseTStates));
            _pulses.Add(new Pulse(false, HalfPulseTStates));
        }

        // The silence between bits is what tells the ROM the burst has ended.
        _pulses.Add(new Pulse(false, BitGapTStates));
    }

    /// <summary>
    /// The EAR level at <paramref name="currentTState"/>. Silence reads as true.
    /// </summary>
    public bool ReadBit(ulong currentTState)
    {
        if (AtEnd) return true;

        // The clock is already running when the tape is first read, so the
        // first call establishes the origin rather than assuming zero.
        if (!_started)
        {
            _started = true;
            _pulseStart = currentTState;
        }

        // A caller that rewinds the clock should not wind the tape backwards.
        if (currentTState < _pulseStart) return _pulses[_index].Level;

        while (!AtEnd && currentTState - _pulseStart >= (ulong)_pulses[_index].TStates)
        {
            _pulseStart += (ulong)_pulses[_index].TStates;
            _index++;
        }

        return AtEnd || _pulses[_index].Level;
    }

    // ── Recording ────────────────────────────────────────────────────────────

    private readonly List<byte> _recorded = [];
    private readonly List<int> _recordedBits = [];
    private bool _lastMic;
    private ulong _lastMicChange;
    private bool _recording;
    private int _pulseCount;

    /// <summary>Bytes decoded from what the machine has saved so far.</summary>
    public IReadOnlyList<byte> RecordedBytes => _recorded;

    /// <summary>Untimed writes cannot be decoded, so they are ignored.</summary>
    /// <remarks>
    /// Present only to satisfy <see cref="ITapeDevice"/>. Silently accepting a
    /// level with no timestamp and guessing a duration would produce plausible
    /// but wrong data, which is worse than recording nothing.
    /// </remarks>
    public void WriteBit(bool bit) { }

    public void WriteBit(bool bit, ulong currentTState)
    {
        if (!_recording)
        {
            _recording = true;
            _lastMic = bit;
            _lastMicChange = currentTState;
            return;
        }

        if (bit == _lastMic)
        {
            // No edge, but a long enough low is the gap that ends a bit.
            if (!bit && currentTState - _lastMicChange >= (ulong)BitGapTStates) FlushBit(currentTState);
            return;
        }

        // A falling edge completes one high pulse.
        if (_lastMic && !bit) _pulseCount++;

        _lastMic = bit;
        _lastMicChange = currentTState;
    }

    private void FlushBit(ulong currentTState)
    {
        _lastMicChange = currentTState;

        if (_pulseCount == 0) return;

        // Nine pulses is a 1 and four a 0; anything else is a damaged bit, and
        // the midpoint is the least-bad reading of it.
        bool one = _pulseCount >= (PulsesForZero + PulsesForOne) / 2;
        _pulseCount = 0;

        _recordedBits.Add(one ? 1 : 0);

        if (_recordedBits.Count == 8)
        {
            int value = 0;
            foreach (int b in _recordedBits) value = (value << 1) | b;
            _recorded.Add((byte)value);
            _recordedBits.Clear();
        }
    }

    /// <summary>Writes everything recorded so far, as a raw memory image.</summary>
    public void Save(Stream destination)
    {
        foreach (byte b in _recorded) destination.WriteByte(b);
    }

    /// <summary>Discards the recording, keeping any loaded tape intact.</summary>
    public void ClearRecording()
    {
        _recorded.Clear();
        _recordedBits.Clear();
        _pulseCount = 0;
        _recording = false;
    }
}
