namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private bool CheckCondition(int cc) => cc switch
    {
        0 => !FlagZ, // NZ
        1 => FlagZ,  // Z
        2 => !FlagC, // NC
        3 => FlagC,  // C
        4 => !FlagPV,// PO
        5 => FlagPV, // PE
        6 => !FlagS, // P
        7 => FlagS,  // M
        _ => throw new ArgumentOutOfRangeException(nameof(cc))
    };

    private void JP_nn() { PC = FetchWord(); TotalCycles += 10UL; }
    
    private void JP_cc_nn(int cc)
    {
        ushort addr = FetchWord();
        if (CheckCondition(cc)) PC = addr;
        TotalCycles += 10UL;
    }

    private void JR_e()
    {
        sbyte offset = (sbyte)Fetch();
        PC = (ushort)(PC + offset);
        TotalCycles += 12UL;
    }

    private void JR_cc_e(int cc)
    {
        sbyte offset = (sbyte)Fetch();
        if (CheckCondition(cc))
        {
            PC = (ushort)(PC + offset);
            TotalCycles += 12UL;
        }
        else
        {
            TotalCycles += 7UL;
        }
    }

    private void CALL_nn()
    {
        ushort addr = FetchWord();
        Push(PC);
        PC = addr;
        TotalCycles += 17UL;
    }

    private void CALL_cc_nn(int cc)
    {
        ushort addr = FetchWord();
        if (CheckCondition(cc))
        {
            Push(PC);
            PC = addr;
            TotalCycles += 17UL;
        }
        else
        {
            TotalCycles += 10UL;
        }
    }

    private void RET() { PC = Pop(); TotalCycles += 10UL; }

    private void DJNZ()
    {
        sbyte offset = (sbyte)Fetch();
        if (--B != 0)
        {
            PC = (ushort)(PC + offset);
            TotalCycles += 13UL;
        }
        else
        {
            TotalCycles += 8UL;
        }
    }

    private void RET_cc(int cc)
    {
        if (CheckCondition(cc))
        {
            PC = Pop();
            TotalCycles += 11UL;
        }
        else
        {
            TotalCycles += 5UL;
        }
    }
}
