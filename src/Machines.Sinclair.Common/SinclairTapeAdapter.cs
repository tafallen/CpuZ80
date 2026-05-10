using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// ITapeDevice implementation for Sinclair pulse-count tape encoding.
/// Used by ZX80 (.o files) and ZX81 (.p files).
///
/// Encoding: pulse-count, MSB-first per byte.
///   0 bit → 4 HIGH+LOW pulse pairs (8 ReadBit() calls)
///   1 bit → 9 HIGH+LOW pulse pairs (18 ReadBit() calls)
/// </summary>
public sealed class SinclairTapeAdapter : ITapeDevice
{
    private readonly Queue<bool> _signal = new();

    public void Load(Stream data)
    {
        _signal.Clear();
        int b;
        while ((b = data.ReadByte()) != -1)
            EnqueueByte((byte)b);
    }

    public bool ReadBit(ulong currentTState) => _signal.Count > 0 ? _signal.Dequeue() : true; // true = silence

    public void WriteBit(bool bit) { /* MIC output not yet implemented for physical file write */ }

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
