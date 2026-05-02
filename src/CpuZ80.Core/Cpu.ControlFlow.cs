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

    private void JP_nn() { PC = FetchWord(); Tick(10); }
    
    private void JP_cc_nn(int cc)
    {
        ushort addr = FetchWord();
        if (CheckCondition(cc)) PC = addr;
        Tick(10);
    }

    private void JR_e()
    {
        sbyte offset = (sbyte)Fetch();
        PC = (ushort)(PC + offset);
        Tick(12);
    }

    private void JR_cc_e(int cc)
    {
        sbyte offset = (sbyte)Fetch();
        if (CheckCondition(cc))
        {
            PC = (ushort)(PC + offset);
            Tick(12);
        }
        else
        {
            Tick(7);
        }
    }

    private void CALL_nn()
    {
        ushort addr = FetchWord();
        Push(PC);
        PC = addr;
        Tick(17);
    }

    private void CALL_cc_nn(int cc)
    {
        ushort addr = FetchWord();
        if (CheckCondition(cc))
        {
            Push(PC);
            PC = addr;
            Tick(17);
        }
        else
        {
            Tick(10);
        }
    }

    private void RET() { PC = Pop(); Tick(10); }

    private void DJNZ()
    {
        sbyte offset = (sbyte)Fetch();
        if (--B != 0)
        {
            PC = (ushort)(PC + offset);
            Tick(13);
        }
        else
        {
            Tick(8);
        }
    }

    private void RET_cc(int cc)
    {
        if (CheckCondition(cc))
        {
            PC = Pop();
            Tick(11);
        }
        else
        {
            Tick(5);
        }
    }
}

