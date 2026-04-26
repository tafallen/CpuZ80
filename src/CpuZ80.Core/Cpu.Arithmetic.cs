namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void DoAdd(byte val) => AddInternal(val, false);
    private void DoAdc(byte val) => AddInternal(val, true);

    private void AddInternal(byte val, bool useCarry)
    {
        int carry = (useCarry && FlagC) ? 1 : 0;
        int res = A + val + carry;
        
        FlagH = ((A ^ val ^ res) & 0x10) != 0;
        FlagPV = (((A ^ res) & (val ^ res)) & 0x80) != 0;
        FlagC = res > 0xFF;
        FlagN = false;
        
        A = (byte)(res & 0xFF);
        
        FlagZ = A == 0;
        FlagS = (A & 0x80) != 0;
        F = (byte)((F & ~0x28) | (A & 0x28));
        
        TotalCycles += 4UL; 
    }

    private byte DoInc(byte val)
    {
        byte res = (byte)(val + 1);
        
        FlagN = false;
        FlagH = (res & 0x0F) == 0;
        FlagPV = val == 0x7F;
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        F = (byte)((F & ~0x28) | (res & 0x28));
        
        return res;
    }

    private byte DoDec(byte val)
    {
        byte res = (byte)(val - 1);
        
        FlagN = true;
        FlagH = (res & 0x0F) == 0x0F;
        FlagPV = val == 0x80;
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        F = (byte)((F & ~0x28) | (res & 0x28));
        
        return res;
    }

    private void DoSub(byte val) => SubInternal(val, false);
    private void DoSbc(byte val) => SubInternal(val, true);

    private void SubInternal(byte val, bool useCarry)
    {
        int carry = (useCarry && FlagC) ? 1 : 0;
        int res = A - val - carry;
        
        FlagH = ((A ^ val ^ res) & 0x10) != 0;
        FlagPV = (((A ^ val) & (A ^ res)) & 0x80) != 0;
        FlagC = res < 0;
        FlagN = true;
        
        A = (byte)(res & 0xFF);
        
        FlagZ = A == 0;
        FlagS = (A & 0x80) != 0;
        F = (byte)((F & ~0x28) | (A & 0x28));
        
        TotalCycles += 4UL; 
    }

    private void DoAdd16(ushort val)
    {
        int res = HL + val;
        
        FlagN = false;
        FlagH = ((HL ^ val ^ res) & 0x1000) != 0;
        FlagC = (res & 0x10000) != 0;
        
        HL = (ushort)(res & 0xFFFF);
        F = (byte)((F & ~0x28) | ((HL >> 8) & 0x28)); 
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
        SubInternal(val, false); 
        A = oldA;
        // Correct X/Y flags for CP: they come from the OPERAND, not the result
        // Wait, no. They come from the OPERAND for BIT, but for CP they come from the result?
        // Actually for CP they come from the operand val? No, result.
        // Let's re-verify: CP result bits 3,5. Yes.
        F = (byte)((F & ~0x28) | (val & 0x28)); // Actually, many sources say Operand for CP!
    }

    private void SetLogicFlags(byte res)
    {
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = GetParity(res);
        F = (byte)((F & ~0x28) | (res & 0x28));
    }

    private bool GetParity(byte val)
    {
        int bits = 0;
        for (int i = 0; i < 8; i++)
            if ((val & (1 << i)) != 0) bits++;
        return (bits % 2) == 0;
    }

    private void RLCA()
    {
        FlagC = (A & 0x80) != 0;
        A = (byte)((A << 1) | (FlagC ? 1 : 0));
        FlagN = false;
        FlagH = false;
        F = (byte)((F & ~0x28) | (A & 0x28));
        TotalCycles += 4UL;
    }

    private void RRCA()
    {
        FlagC = (A & 0x01) != 0;
        A = (byte)((A >> 1) | (FlagC ? 0x80 : 0));
        FlagN = false;
        FlagH = false;
        F = (byte)((F & ~0x28) | (A & 0x28));
        TotalCycles += 4UL;
    }

    private void RLA()
    {
        bool oldC = FlagC;
        FlagC = (A & 0x80) != 0;
        A = (byte)((A << 1) | (oldC ? 1 : 0));
        FlagN = false;
        FlagH = false;
        F = (byte)((F & ~0x28) | (A & 0x28));
        TotalCycles += 4UL;
    }

    private void RRA()
    {
        bool oldC = FlagC;
        FlagC = (A & 0x01) != 0;
        A = (byte)((A >> 1) | (oldC ? 0x80 : 0));
        FlagN = false;
        FlagH = false;
        F = (byte)((F & ~0x28) | (A & 0x28));
        TotalCycles += 4UL;
    }

    private void DAA()
    {
        byte adj = 0;
        bool oldC = FlagC;
        if (FlagH || (A & 0x0F) > 9) adj |= 6;
        if (FlagC || A > 0x99)
        {
            adj |= 0x60;
            FlagC = true;
        }

        if (FlagN)
        {
            FlagH = (A & 0x0F) < (adj & 0x0F);
            A -= adj;
        }
        else
        {
            FlagH = (A & 0x0F) + (adj & 0x0F) > 0x0F;
            A += adj;
        }

        FlagZ = A == 0;
        FlagS = (A & 0x80) != 0;
        FlagPV = GetParity(A);
        F = (byte)((F & ~0x28) | (A & 0x28));
        TotalCycles += 4UL;
    }
}
