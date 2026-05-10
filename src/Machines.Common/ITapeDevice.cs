namespace Machines.Common;

/// <summary>
/// Represents a virtual tape deck.
/// Implementations load a .tap / .p / .wav file and stream bits to the chip,
/// or capture bits written by the chip back to a file.
/// </summary>
public interface ITapeDevice
{
    bool ReadBit(ulong currentTState);
    void WriteBit(bool bit);

    /// <summary>Load tape data from a stream (e.g. a .tap or .p file).</summary>
    void Load(Stream data);
}
