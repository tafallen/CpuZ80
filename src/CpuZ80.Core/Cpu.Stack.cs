namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void Push(ushort val)
    {
        _bus.Write(--SP, (byte)(val >> 8)); // High byte first
        _bus.Write(--SP, (byte)(val & 0xFF)); // Low byte second
    }

    private ushort Pop()
    {
        byte lo = _bus.Read(SP++);
        byte hi = _bus.Read(SP++);
        return (ushort)((hi << 8) | lo);
    }
}

