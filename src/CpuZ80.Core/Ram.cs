namespace CpuZ80.Core;

public sealed class Ram : IBus
{
    private readonly byte[] _data;

    public Ram(int size)
    {
        _data = new byte[size];
    }

    public byte Read(ushort address) => _data[address];
    public void Write(ushort address, byte value) => _data[address] = value;

    public void Load(ushort address, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
            _data[address + i] = data[i];
    }
}
