using Machines.Common;

namespace Machines.Zx81;

/// <summary>
/// ITapeDevice implementation for ZX81 .p tape files.
/// 
/// The .p format is a raw RAM dump from 0x4000 to E_LINE.
/// Encoding is identical to ZX80 (pulse-count).
/// </summary>
public sealed class Zx81TapeAdapter : ITapeDevice
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

    public void WriteBit(bool bit) { }

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
