using Machines.Common;

namespace Machines.AmstradCpc;

/// <summary>
/// A cassette loaded from a <c>.CDT</c> image.
/// </summary>
/// <remarks>
/// <c>.CDT</c> is the TZX format under a different extension: the file says
/// <c>ZXTape!</c> either way, and only the name distinguishes a CPC tape from a
/// Spectrum one.
///
/// <b>Every timing in the file is in Spectrum T-states at 3.5 MHz.</b> A CPC
/// runs at 4 MHz, so all of them are scaled by 4/3.5 on the way in. Playing
/// them unscaled makes every pulse about 14% short, which is inside what a
/// forgiving loader tolerates and outside what a tight one does — so some tapes
/// would load and others would fail for no visible reason.
///
/// See docs/amstrad-cpc-tape.md.
/// </remarks>
public sealed class CdtTape : ITapeDevice
{
    /// <summary>The clock every TZX timing is expressed in.</summary>
    public const int TzxClockHz = 3_500_000;

    /// <summary>A level and how long it lasts, in host T-states.</summary>
    private readonly record struct Pulse(bool Level, int TStates);

    private readonly List<Pulse> _pulses = [];
    private int _index;
    private ulong _pulseStart;
    private bool _started;

    /// <summary>Host clock, used to scale the file's 3.5 MHz timings.</summary>
    public int ClockHz { get; }

    /// <summary>Descriptions and titles found in the file's metadata blocks.</summary>
    public IReadOnlyList<string> Descriptions => _descriptions;
    private readonly List<string> _descriptions = [];

    /// <summary>Data blocks the file contained, in order.</summary>
    public int DataBlockCount { get; private set; }

    /// <summary>True once the whole tape has played out.</summary>
    public bool AtEnd => _index >= _pulses.Count;

    /// <summary>Total playing time, in host T-states.</summary>
    public ulong LengthInTStates
    {
        get
        {
            ulong total = 0;
            foreach (var pulse in _pulses) total += (ulong)pulse.TStates;
            return total;
        }
    }

    public CdtTape(int clockHz = 4_000_000) => ClockHz = clockHz;

    /// <summary>Converts a TZX T-state count into host T-states.</summary>
    private int Scale(int tzxTStates) => (int)((long)tzxTStates * ClockHz / TzxClockHz);

    private int Milliseconds(int ms) => (int)((long)ms * ClockHz / 1000);

    // ── Loading ──────────────────────────────────────────────────────────────

    public void Load(Stream data)
    {
        _pulses.Clear();
        _descriptions.Clear();
        DataBlockCount = 0;
        _index = 0;
        _started = false;

        using var reader = new BinaryReader(data, System.Text.Encoding.ASCII, leaveOpen: true);

        byte[] signature = reader.ReadBytes(8);
        if (signature.Length < 8 ||
            System.Text.Encoding.ASCII.GetString(signature, 0, 7) != "ZXTape!" ||
            signature[7] != 0x1A)
        {
            throw new InvalidDataException(
                "Not a .CDT or .TZX image: the file does not start with \"ZXTape!\".");
        }

        reader.ReadByte();   // major version
        reader.ReadByte();   // minor version

        while (data.Position < data.Length)
        {
            byte id = reader.ReadByte();
            ReadBlock(reader, id);
        }
    }

    private void ReadBlock(BinaryReader reader, byte id)
    {
        switch (id)
        {
            case 0x10: ReadStandardSpeedBlock(reader); break;
            case 0x11: ReadTurboSpeedBlock(reader); break;
            case 0x12: ReadPureTone(reader); break;
            case 0x13: ReadPulseSequence(reader); break;
            case 0x14: ReadPureData(reader); break;
            case 0x20: ReadPause(reader); break;

            // Grouping and metadata carry no signal. They are skipped rather
            // than rejected: a file that describes itself is still a valid tape,
            // and refusing to load one would be a needless failure.
            case 0x21: SkipCounted(reader, reader.ReadByte()); break;
            case 0x22: break;                                        // group end
            case 0x30: SkipCounted(reader, reader.ReadByte(), text: true); break;
            case 0x31: reader.ReadByte(); SkipCounted(reader, reader.ReadByte(), text: true); break;
            case 0x32: ReadArchiveInfo(reader); break;
            case 0x33: SkipCounted(reader, reader.ReadByte() * 3); break;
            case 0x35: ReadCustomInfo(reader); break;
            case 0x5A: reader.ReadBytes(9); break;                   // glue block

            default:
                throw new InvalidDataException(
                    $"Unsupported .CDT block 0x{id:X2} at offset {reader.BaseStream.Position - 1}. " +
                    "Loading further would produce a tape that plays the wrong thing rather than failing.");
        }
    }

    private void SkipCounted(BinaryReader reader, int count, bool text = false)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (text && bytes.Length > 0)
        {
            _descriptions.Add(System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'));
        }
    }

    private void ReadArchiveInfo(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        byte[] body = reader.ReadBytes(length);

        // The first string is the title, which is worth surfacing.
        if (body.Length > 3)
        {
            int textLength = Math.Min(body[2], body.Length - 3);
            if (textLength > 0)
            {
                _descriptions.Add(System.Text.Encoding.ASCII.GetString(body, 3, textLength).TrimEnd('\0'));
            }
        }
    }

    private void ReadCustomInfo(BinaryReader reader)
    {
        reader.ReadBytes(16);                       // identification string
        int length = (int)reader.ReadUInt32();
        reader.ReadBytes(length);
    }

    /// <summary>
    /// Block 0x10: a data block at the Spectrum ROM's own timings.
    /// </summary>
    /// <remarks>
    /// Rare in a CPC tape, which normally uses 0x11 with the CPC's own rates,
    /// but a converted Spectrum tape or a header block can still use it.
    /// </remarks>
    private void ReadStandardSpeedBlock(BinaryReader reader)
    {
        int pause = reader.ReadUInt16();
        int length = reader.ReadUInt16();
        byte[] data = reader.ReadBytes(length);

        // The pilot is long for a header and short for data, decided by the
        // first byte's top bit.
        int pilotCount = data.Length > 0 && data[0] < 128 ? 8063 : 3223;

        EmitDataBlock(data, 2168, 667, 735, 855, 1710, pilotCount, 8, pause);
    }

    /// <summary>Block 0x11: a data block carrying its own timings.</summary>
    private void ReadTurboSpeedBlock(BinaryReader reader)
    {
        int pilot = reader.ReadUInt16();
        int sync1 = reader.ReadUInt16();
        int sync2 = reader.ReadUInt16();
        int zero = reader.ReadUInt16();
        int one = reader.ReadUInt16();
        int pilotCount = reader.ReadUInt16();
        int usedBits = reader.ReadByte();
        int pause = reader.ReadUInt16();
        int length = ReadTriple(reader);
        byte[] data = reader.ReadBytes(length);

        EmitDataBlock(data, pilot, sync1, sync2, zero, one, pilotCount, usedBits, pause);
    }

    /// <summary>Block 0x12: a run of identical pulses, with no data.</summary>
    private void ReadPureTone(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        int count = reader.ReadUInt16();

        for (int i = 0; i < count; i++) EmitPulse(length);
    }

    /// <summary>Block 0x13: a handful of pulses of individually given lengths.</summary>
    private void ReadPulseSequence(BinaryReader reader)
    {
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++) EmitPulse(reader.ReadUInt16());
    }

    /// <summary>Block 0x14: data with no pilot or sync ahead of it.</summary>
    private void ReadPureData(BinaryReader reader)
    {
        int zero = reader.ReadUInt16();
        int one = reader.ReadUInt16();
        int usedBits = reader.ReadByte();
        int pause = reader.ReadUInt16();
        int length = ReadTriple(reader);
        byte[] data = reader.ReadBytes(length);

        EmitData(data, zero, one, usedBits);
        EmitPause(pause);
        DataBlockCount++;
    }

    /// <summary>Block 0x20: silence, or a request to stop the tape.</summary>
    private void ReadPause(BinaryReader reader) => EmitPause(reader.ReadUInt16());

    private static int ReadTriple(BinaryReader reader)
    {
        int lo = reader.ReadByte();
        int mid = reader.ReadByte();
        int hi = reader.ReadByte();
        return lo | (mid << 8) | (hi << 16);
    }

    // ── Turning blocks into pulses ───────────────────────────────────────────

    private bool _level;

    private void EmitPulse(int tzxLength)
    {
        // Each pulse is a level held for its length, and the level alternates —
        // so a pulse is a half-wave, not a whole one.
        _pulses.Add(new Pulse(_level, Math.Max(1, Scale(tzxLength))));
        _level = !_level;
    }

    private void EmitPause(int milliseconds)
    {
        if (milliseconds <= 0) return;

        // A pause is silence, and the line rests low afterwards regardless of
        // where the last pulse left it.
        _pulses.Add(new Pulse(false, Milliseconds(milliseconds)));
        _level = false;
    }

    private void EmitDataBlock(
        byte[] data, int pilot, int sync1, int sync2,
        int zero, int one, int pilotCount, int usedBits, int pause)
    {
        for (int i = 0; i < pilotCount; i++) EmitPulse(pilot);

        EmitPulse(sync1);
        EmitPulse(sync2);

        EmitData(data, zero, one, usedBits);
        EmitPause(pause);
        DataBlockCount++;
    }

    private void EmitData(byte[] data, int zero, int one, int usedBits)
    {
        if (usedBits is < 1 or > 8) usedBits = 8;

        for (int i = 0; i < data.Length; i++)
        {
            // The final byte may carry fewer than eight meaningful bits, and
            // playing the padding would append bits the loader never expects.
            int bits = i == data.Length - 1 ? usedBits : 8;

            for (int bit = 7; bit >= 8 - bits; bit--)
            {
                int length = ((data[i] >> bit) & 1) != 0 ? one : zero;

                // Every bit is two pulses of the same length.
                EmitPulse(length);
                EmitPulse(length);
            }
        }
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The cassette read level at <paramref name="currentTState"/>. A finished
    /// or empty tape reads low.
    /// </summary>
    public bool ReadBit(ulong currentTState)
    {
        if (AtEnd) return false;

        // The machine's clock is already running when the motor starts, so the
        // first read sets the origin rather than assuming zero.
        if (!_started)
        {
            _started = true;
            _pulseStart = currentTState;
        }

        if (currentTState < _pulseStart) return _pulses[_index].Level;

        while (!AtEnd && currentTState - _pulseStart >= (ulong)_pulses[_index].TStates)
        {
            _pulseStart += (ulong)_pulses[_index].TStates;
            _index++;
        }

        return !AtEnd && _pulses[_index].Level;
    }

    /// <summary>Rewinds to the start of the tape.</summary>
    public void Rewind()
    {
        _index = 0;
        _started = false;
    }

    /// <summary>Saving to a .CDT is not implemented; playback only.</summary>
    public void WriteBit(bool bit) { }

    public void WriteBit(bool bit, ulong currentTState) { }
}
