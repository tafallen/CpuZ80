namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private readonly Action[] _cbOps = new Action[256];

    private void BuildCbDispatchTable()
    {
        for (int i = 0; i < 256; i++)
        {
            int opcode = i;
            int bit = (opcode >> 3) & 0x07;
            int reg = opcode & 0x07;

            if (opcode < 0x40) // Rotates and Shifts
            {
                int type = (opcode >> 3) & 0x07;
                _cbOps[opcode] = () => { SetReg(reg, DoShift(type, GetReg(reg))); Tick((reg == 6) ? 15 : 8); };
            }
            else if (opcode < 0x80) // BIT
            {
                _cbOps[opcode] = () => { 
                    byte val = GetReg(reg);
                    DoBit(bit, val); 
                    if (reg == 6) SetUndocumentedFlagsFromWZ();
                    Tick((reg == 6) ? 12 : 8); 
                };
            }
            else if (opcode < 0xC0) // RES
            {
                _cbOps[opcode] = () => { SetReg(reg, (byte)(GetReg(reg) & ~(1 << bit))); Tick((reg == 6) ? 15 : 8); };
            }
            else // SET
            {
                _cbOps[opcode] = () => { SetReg(reg, (byte)(GetReg(reg) | (1 << bit))); Tick((reg == 6) ? 15 : 8); };
            }
        }
    }

    private byte DoShift(int type, byte val)
    {
        byte res = 0;
        bool oldC = FlagC;

        switch (type)
        {
            case 0: // RLC
                FlagC = (val & 0x80) != 0;
                res = (byte)((val << 1) | (FlagC ? 1 : 0));
                break;
            case 1: // RRC
                FlagC = (val & 0x01) != 0;
                res = (byte)((val >> 1) | (FlagC ? 0x80 : 0));
                break;
            case 2: // RL
                FlagC = (val & 0x80) != 0;
                res = (byte)((val << 1) | (oldC ? 1 : 0));
                break;
            case 3: // RR
                FlagC = (val & 0x01) != 0;
                res = (byte)((val >> 1) | (oldC ? 0x80 : 0));
                break;
            case 4: // SLA
                FlagC = (val & 0x80) != 0;
                res = (byte)(val << 1);
                break;
            case 5: // SRA
                FlagC = (val & 0x01) != 0;
                res = (byte)((val >> 1) | (val & 0x80));
                break;
            case 6: // SLL (Undocumented, but often implemented as SLA then bit 0 = 1)
                FlagC = (val & 0x80) != 0;
                res = (byte)((val << 1) | 1);
                break;
            case 7: // SRL
                FlagC = (val & 0x01) != 0;
                res = (byte)(val >> 1);
                break;
        }

        FlagN = false;
        FlagH = false;
        SetLogicFlags(res);
        return res;
    }

    private void DoBit(int bit, byte val)
    {
        FlagZ = (val & (1 << bit)) == 0;
        FlagN = false;
        FlagH = true;
        FlagPV = FlagZ;
        FlagS = bit == 7 && !FlagZ;
        // Zilog Z80: bits 3 and 5 are copies of bits 3 and 5 of the tested register (operand)
        F = (byte)((F & ~0x28) | (val & 0x28));
    }

    private void HandleCB()
    {
        byte opcode = Fetch();
        _cbOps[opcode]();
    }
}
