namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void DoAdd(byte val) => AddInternal(val, false);
    private void DoAdc(byte val) => AddInternal(val, true);

    private void AddInternal(byte val, bool useCarry)
    {
        int carry = (useCarry && FlagC) ? 1 : 0;
        int res = A + val + carry;
        
        // Flags
        FlagN = false;
        FlagH = ((A & 0x0F) + (val & 0x0F) + carry > 0x0F);
        FlagPV = (((A ^ res) & (val ^ res)) & 0x80) != 0;
        FlagC = res > 0xFF;
        
        A = (byte)(res & 0xFF);
        
        FlagZ = A == 0;
        FlagS = (A & 0x80) != 0;
        
        TotalCycles += 4UL; // Base cycles for ADD/ADC A, r
    }
}
