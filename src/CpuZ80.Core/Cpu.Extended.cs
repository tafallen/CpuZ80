namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private ushort DoAdc16(ushort cur, ushort val)
    {
        int carry = FlagC ? 1 : 0;
        int res = cur + val + carry;
        
        ushort result = (ushort)(res & 0xFFFF);

        FlagN = false;
        FlagH = (((cur & 0x0FFF) + (val & 0x0FFF) + carry) & 0x1000) != 0;
        FlagPV = (((cur ^ res) & (val ^ res)) & 0x8000) != 0;
        FlagC = res > 0xFFFF;
        FlagS = (result & 0x8000) != 0;
        FlagZ = result == 0;

        WZ = (ushort)(cur + 1);
        SetUndocumentedFlags((byte)(result >> 8));
        return result;
    }

    private ushort DoSbc16(ushort cur, ushort val)
    {
        int carry = FlagC ? 1 : 0;
        int res = cur - val - carry;

        ushort result = (ushort)(res & 0xFFFF);

        FlagN = true;
        FlagH = (((cur & 0x0FFF) - (val & 0x0FFF) - carry) & 0x1000) != 0;
        FlagPV = (((cur ^ val) & (cur ^ res)) & 0x8000) != 0;
        FlagC = res < 0;
        FlagS = (result & 0x8000) != 0;
        FlagZ = result == 0;

        WZ = (ushort)(cur + 1);
        SetUndocumentedFlags((byte)(result >> 8));
        return result;
    }
    private void LDI()
    {
        byte val = Read(HL++);
        Write(DE++, val);
        BC--;
        FlagN = false;
        FlagH = false;
        FlagPV = BC != 0;

        // Undocumented flags: Bit 5 = (A+val) bit 1, Bit 3 = (A+val) bit 3
        byte res = (byte)(A + val);
        F = (byte)((F & ~0x28) | (res & 0x08) | ((res << 4) & 0x20));
    }

    private void LDIR()
    {
        LDI();
        if (BC != 0)
        {
            PC -= 2; // Repeat LDIR (ED B0)
            Tick(5); // 21 cycles total when repeating
        }
    }

    private void LDD()
    {
        byte val = Read(HL--);
        Write(DE--, val);
        BC--;
        FlagN = false;
        FlagH = false;
        FlagPV = BC != 0;

        // Undocumented flags
        byte res = (byte)(A + val);
        F = (byte)((F & ~0x28) | (res & 0x08) | ((res << 4) & 0x20));
    }

    private void LDDR()
    {
        LDD();
        if (BC != 0)
        {
            PC -= 2;
            Tick(5);
        }
    }

    private void CPI()
    {
        byte val = Read(HL++);
        byte res = (byte)(A - val);
        BC--;
        WZ++;
        FlagN = true;
        FlagH = (A & 0x0F) < (val & 0x0F);
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = BC != 0;

        // Undocumented flags
        byte res2 = (byte)(res - (FlagH ? 1 : 0));
        F = (byte)((F & ~0x28) | (res2 & 0x08) | ((res2 << 4) & 0x20));
    }

    private void CPIR()
    {
        CPI();
        if (BC != 0 && !FlagZ)
        {
            PC -= 2;
            Tick(5);
        }
    }

    private void CPD()
    {
        byte val = Read(HL--);
        byte res = (byte)(A - val);
        BC--;
        WZ--;
        FlagN = true;
        FlagH = (A & 0x0F) < (val & 0x0F);
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = BC != 0;

        // Undocumented flags
        byte res2 = (byte)(res - (FlagH ? 1 : 0));
        F = (byte)((F & ~0x28) | (res2 & 0x08) | ((res2 << 4) & 0x20));
    }

    private void CPDR()
    {
        CPD();
        if (BC != 0 && !FlagZ)
        {
            PC -= 2;
            Tick(5);
        }
    }

    private void NEG()
    {
        byte val = A;
        A = 0;
        SubInternal(val, false);
    }

    private void RETI() { RET(); IFF1 = IFF2; }
    private void RETN() { RET(); IFF1 = IFF2; }

    private void RET() { PC = Pop(); }

    private void RRD()
    {
        byte mem = Read(HL);
        byte lowA = (byte)(A & 0x0F);
        A = (byte)((A & 0xF0) | (mem & 0x0F));
        mem = (byte)((lowA << 4) | (mem >> 4));
        Write(HL, mem);
        FlagH = false;
        FlagN = false;
        SetLogicFlags(A);
    }

    private void INI()
    {
        byte val = _ports?.In(BC) ?? 0xFF;
        Write(HL++, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
    }

    private void INIR()
    {
        INI();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void IND()
    {
        byte val = _ports?.In(BC) ?? 0xFF;
        Write(HL--, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
    }

    private void INDR()
    {
        IND();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void OUTI()
    {
        byte val = Read(HL++);
        _ports?.Out(BC, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
    }

    private void OTIR()
    {
        OUTI();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void OUTD()
    {
        byte val = Read(HL--);
        _ports?.Out(BC, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
    }

    private void OTDR()
    {
        OUTD();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void RLD()
    {
        byte mem = Read(HL);
        byte lowA = (byte)(A & 0x0F);
        A = (byte)((A & 0xF0) | (mem >> 4));
        mem = (byte)((mem << 4) | lowA);
        Write(HL, mem);
        FlagH = false;
        FlagN = false;
        SetLogicFlags(A);
    }
}

