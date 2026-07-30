namespace CpuZ80.Core;

public sealed partial class Cpu
{
    // Stack traffic goes through Read/Write, not _bus directly, so that
    // ICpuHost sees it and machines can apply contention to CALL/RET/RST.
    private void Push(ushort val)
    {
        Write(--SP, (byte)(val >> 8)); // High byte first
        Write(--SP, (byte)(val & 0xFF)); // Low byte second
    }

    private ushort Pop()
    {
        byte lo = Read(SP++);
        byte hi = Read(SP++);
        return (ushort)((hi << 8) | lo);
    }
}

