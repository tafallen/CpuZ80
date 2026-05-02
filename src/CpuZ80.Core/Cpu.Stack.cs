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
    
    // PUSH ss
    private void PUSH_BC() { Push(BC); Tick(11); }
    private void PUSH_DE() { Push(DE); Tick(11); }
    private void PUSH_HL() { Push(GetReg16(2)); Tick(11); }
    private void PUSH_AF() { Push((ushort)((A << 8) | F)); Tick(11); }

    // POP ss
    private void POP_BC() { BC = Pop(); Tick(10); }
    private void POP_DE() { DE = Pop(); Tick(10); }
    private void POP_HL() { SetReg16(2, Pop()); Tick(10); }
    private void POP_AF() 
    { 
        ushort val = Pop();
        A = (byte)(val >> 8);
        F = (byte)(val & 0xFF);
        Tick(10); 
    }
}

