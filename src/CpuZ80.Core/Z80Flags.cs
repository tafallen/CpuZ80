namespace CpuZ80.Core;

[Flags]
public enum Z80Flags : byte
{
    None = 0,
    Carry = 1 << 0,         // C
    AddSub = 1 << 1,        // N (1 for subtraction, 0 for addition)
    ParityOverflow = 1 << 2, // P/V (Parity for logic, Overflow for arithmetic)
    Unused3 = 1 << 3,       // X (Undocumented/unused)
    HalfCarry = 1 << 4,     // H
    Unused5 = 1 << 5,       // Y (Undocumented/unused)
    Zero = 1 << 6,          // Z
    Sign = 1 << 7           // S
}
