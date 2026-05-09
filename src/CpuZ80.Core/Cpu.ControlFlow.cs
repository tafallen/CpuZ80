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
}

