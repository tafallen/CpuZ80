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

    private byte DoInc(byte val)
    {
        byte res = (byte)(val + 1);
        
        FlagN = false;
        FlagH = (val & 0x0F) == 0x0F;
        FlagPV = val == 0x7F;
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        
        return res;
    }

    private byte DoDec(byte val)
    {
        byte res = (byte)(val - 1);
        
        FlagN = true;
        FlagH = (val & 0x0F) == 0x00;
        FlagPV = val == 0x80;
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        
        return res;
    }

    private void DoSub(byte val) => SubInternal(val, false);
    private void DoSbc(byte val) => SubInternal(val, true);

    private void SubInternal(byte val, bool useCarry)
    {
        int carry = (useCarry && FlagC) ? 1 : 0;
        int res = A - val - carry;
        
        // Flags
        FlagN = true;
        FlagH = ((A & 0x0F) - (val & 0x0F) - carry < 0);
        FlagPV = (((A ^ val) & (A ^ res)) & 0x80) != 0;
        FlagC = res < 0;
        
        A = (byte)(res & 0xFF);
        
        FlagZ = A == 0;
        FlagS = (A & 0x80) != 0;
        
        TotalCycles += 4UL; // Base cycles for SUB/SBC A, r
    }

    private void DoAdd16(ushort val)
    {
        int res = HL + val;
        
        FlagN = false;
        // H is set if carry from bit 11
        FlagH = ((HL & 0x0FFF) + (val & 0x0FFF) > 0x0FFF);
        FlagC = res > 0xFFFF;
        
        HL = (ushort)(res & 0xFFFF);
        
        TotalCycles += 11UL;
    }

    private void DoAnd(byte val)
    {
        A &= val;
        FlagN = false;
        FlagH = true;
        FlagC = false;
        SetLogicFlags(A);
        TotalCycles += 4UL;
    }

    private void DoOr(byte val)
    {
        A |= val;
        FlagN = false;
        FlagH = false;
        FlagC = false;
        SetLogicFlags(A);
        TotalCycles += 4UL;
    }

    private void DoXor(byte val)
    {
        A ^= val;
        FlagN = false;
        FlagH = false;
        FlagC = false;
        SetLogicFlags(A);
        TotalCycles += 4UL;
    }

    private void DoCp(byte val)
    {
        byte oldA = A;
        SubInternal(val, false); // CP is SUB but result is discarded
        A = oldA;
    }

    private void SetLogicFlags(byte res)
    {
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = GetParity(res);
    }

    private bool GetParity(byte val)
    {
        int bits = 0;
        for (int i = 0; i < 8; i++)
            if ((val & (1 << i)) != 0) bits++;
        return (bits % 2) == 0;
    }
}
