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
        SetUndocumentedFlags(A);
    }

    private byte DoInc(byte val)
    {
        byte res = (byte)(val + 1);
        
        FlagN = false;
        FlagH = (res & 0x0F) == 0;
        FlagPV = val == 0x7F;
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        SetUndocumentedFlags(res);
        
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
        SetUndocumentedFlags(res);
        
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
        SetUndocumentedFlags(A);
    }

    private ushort DoAdd16(ushort cur, ushort val)
    {
        int res = cur + val;

        FlagN = false;
        FlagH = ((cur ^ val ^ res) & 0x1000) != 0;
        FlagC = (res & 0x10000) != 0;

        ushort result = (ushort)(res & 0xFFFF);
        WZ = (ushort)(cur + 1);
        SetUndocumentedFlags((byte)(result >> 8));
        return result;
    }

    private void DoAnd(byte val)
    {
        A &= val;
        FlagN = false;
        FlagH = true;
        FlagC = false;
        SetLogicFlags(A);
    }

    private void DoOr(byte val)
    {
        A |= val;
        FlagN = false;
        FlagH = false;
        FlagC = false;
        SetLogicFlags(A);
    }

    private void DoXor(byte val)
    {
        A ^= val;
        FlagN = false;
        FlagH = false;
        FlagC = false;
        SetLogicFlags(A);
    }

    private void DoCp(byte val)
    {
        byte oldA = A;
        SubInternal(val, false);
        A = oldA;
        SetUndocumentedFlags(val); // X/Y flags come from operand, not result
    }

    internal void SetUndocumentedFlagsFromWZ()
    {
        // Bits 11 and 13 of WZ leak into bits 3 and 5 of F
        SetUndocumentedFlags((byte)(WZ >> 8));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void SetUndocumentedFlags(byte source)
    {
        F = (byte)((F & ~0x28) | (source & 0x28));
    }

    private void SetLogicFlags(byte res)
    {
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = GetParity(res);
        SetUndocumentedFlags(res);
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
        SetUndocumentedFlags(A);
    }

    private void RRCA()
    {
        FlagC = (A & 0x01) != 0;
        A = (byte)((A >> 1) | (FlagC ? 0x80 : 0));
        FlagN = false;
        FlagH = false;
        SetUndocumentedFlags(A);
    }

    private void RLA()
    {
        bool oldC = FlagC;
        FlagC = (A & 0x80) != 0;
        A = (byte)((A << 1) | (oldC ? 1 : 0));
        FlagN = false;
        FlagH = false;
        SetUndocumentedFlags(A);
    }

    private void RRA()
    {
        bool oldC = FlagC;
        FlagC = (A & 0x01) != 0;
        A = (byte)((A >> 1) | (oldC ? 0x80 : 0));
        FlagN = false;
        FlagH = false;
        SetUndocumentedFlags(A);
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
        SetUndocumentedFlags(A);
    }

    public void EX_AF_AF()
    {
        (A, A_) = (A_, A);
        (F, F_) = (F_, F);
    }

    public void EXX()
    {
        (B, B_) = (B_, B);
        (C, C_) = (C_, C);
        (D, D_) = (D_, D);
        (E, E_) = (E_, E);
        (H, H_) = (H_, H);
        (L, L_) = (L_, L);
    }
}

