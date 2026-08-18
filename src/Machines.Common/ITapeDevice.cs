namespace Machines.Common;

/// <summary>
/// Represents a virtual tape deck.
/// Implementations load a .tap / .p / .wav file and stream bits to the chip,
/// or capture bits written by the chip back to a file.
/// </summary>
public interface ITapeDevice
{
    bool ReadBit(ulong currentTState);

    /// <summary>Untimed MIC write. Prefer the timed overload.</summary>
    void WriteBit(bool bit);

    /// <summary>
    /// MIC output with the time it happened, so a recording device can measure
    /// pulse widths.
    /// </summary>
    /// <remarks>
    /// Tape encoding is entirely about durations, so a bare level with no
    /// timestamp cannot be decoded back into data. Defaulted to the untimed
    /// call so existing devices are unaffected.
    /// </remarks>
    void WriteBit(bool bit, ulong currentTState) => WriteBit(bit);

    /// <summary>Load tape data from a stream (e.g. a .tap or .p file).</summary>
    void Load(Stream data);
}
