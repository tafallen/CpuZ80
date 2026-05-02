namespace CpuZ80.Core;

public interface IBus
{
    byte Read(ushort address);
    void Write(ushort address, byte value);
}

