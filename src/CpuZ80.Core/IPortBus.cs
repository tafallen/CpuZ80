namespace CpuZ80.Core;

public interface IPortBus
{
    byte In(ushort port);
    void Out(ushort port, byte value);
}

