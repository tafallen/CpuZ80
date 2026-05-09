namespace CpuZ80.Core;

public sealed partial class Cpu
{
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
        SetUndocumentedFlags(val);
    }
}
