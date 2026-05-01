using Machines.Common;

namespace Machines.Zx80;

/// <summary>
/// ITapeDevice implementation for ZX80 .o / .80 tape files.
///
/// Encoding: pulse-count, MSB-first per byte.
///   0 bit → 4 HIGH+LOW pulse pairs (8 ReadBit() calls)
///   1 bit → 9 HIGH+LOW pulse pairs (18 ReadBit() calls)
///
/// Note: this implementation operates at the signal level, not cycle-level.
/// ReadBit() returns successive HIGH/LOW states; the ROM's sampling loop counts
/// transitions. Cycle-accurate timing is not modelled (ITapeDevice has no cycle
/// parameter). The adapter is suitable for unit testing; integration with the
/// actual ZX80 ROM load routine requires cycle-accurate timing not yet supported.
/// </summary>
public sealed class Zx80TapeAdapter : ITapeDevice
{
    private readonly Queue<bool> _signal = new();

    public void Load(Stream data)
    {
        _signal.Clear();
        int b;
        while ((b = data.ReadByte()) != -1)
            EnqueueByte((byte)b);
    }

    public bool ReadBit() => _signal.Count > 0 ? _signal.Dequeue() : true; // true = silence

    public void WriteBit(bool bit) { /* save direction not yet implemented */ }

    private void EnqueueByte(byte b)
    {
        for (int i = 7; i >= 0; i--)
            EnqueueBit(((b >> i) & 1) != 0);
    }

    private void EnqueueBit(bool one)
    {
        int pulses = one ? 9 : 4;
        for (int p = 0; p < pulses; p++)
        {
            _signal.Enqueue(true);  // HIGH
            _signal.Enqueue(false); // LOW
        }
    }
}
