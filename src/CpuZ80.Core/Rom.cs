namespace CpuZ80.Core;

public sealed class Rom : IBus
{
    private readonly byte[] _data;

    public int Size => _data.Length;
    public ReadOnlySpan<byte> RawBytes => _data;

    public Rom(byte[] data) => _data = (byte[])data.Clone();

    public byte Read(ushort address) => _data[address];

    public void Write(ushort address, byte value) { }
}
