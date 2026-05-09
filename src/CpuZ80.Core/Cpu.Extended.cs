namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private readonly Action[] _edOps = new Action[256];
    public byte I;
    public byte R;
    private int _interruptMode = 0;

    private void BuildEdDispatchTable()
    {
        for (int i = 0; i < 256; i++)
        {
            _edOps[i] = () => { Tick(8); }; // invalid ED opcodes act as NOPs
        }

        // 16-bit ADC/SBC
        _edOps[0x4A] = () => DoAdc16(BC);
        _edOps[0x5A] = () => DoAdc16(DE);
        _edOps[0x6A] = () => DoAdc16(HL);
        _edOps[0x7A] = () => DoAdc16(SP);

        _edOps[0x42] = () => DoSbc16(BC);
        _edOps[0x52] = () => DoSbc16(DE);
        _edOps[0x62] = () => DoSbc16(HL);
        _edOps[0x72] = () => DoSbc16(SP);

        // RRD / RLD
        _edOps[0x67] = RRD;
        _edOps[0x6F] = RLD;

        // LD dd, (nn) / LD (nn), dd
        _edOps[0x4B] = () => { BC = ReadWord(FetchWord()); Tick(20); };
        _edOps[0x5B] = () => { DE = ReadWord(FetchWord()); Tick(20); };
        _edOps[0x7B] = () => { SP = ReadWord(FetchWord()); Tick(20); };

        _edOps[0x43] = () => { WriteWord(FetchWord(), BC); Tick(20); };
        _edOps[0x53] = () => { WriteWord(FetchWord(), DE); Tick(20); };
        _edOps[0x73] = () => { WriteWord(FetchWord(), SP); Tick(20); };

        // LD I, A / LD R, A and vice-versa
        _edOps[0x47] = () => { I = A; Tick(9); };
        _edOps[0x4F] = () => { R = A; Tick(9); };
        _edOps[0x57] = () => { A = I; SetLogicFlags(A); FlagPV = IFF2; FlagN = false; FlagH = false; Tick(9); };
        _edOps[0x5F] = () => { A = R; SetLogicFlags(A); FlagPV = IFF2; FlagN = false; FlagH = false; Tick(9); };

        // IM x
        _edOps[0x46] = () => { _interruptMode = 0; Tick(8); };
        _edOps[0x56] = () => { _interruptMode = 1; Tick(8); };
        _edOps[0x5E] = () => { _interruptMode = 2; Tick(8); };
        // IM aliases
        _edOps[0x4E] = () => { _interruptMode = 0; Tick(8); };
        _edOps[0x66] = () => { _interruptMode = 0; Tick(8); };
        _edOps[0x6E] = () => { _interruptMode = 0; Tick(8); };
        _edOps[0x76] = () => { _interruptMode = 1; Tick(8); };
        _edOps[0x7E] = () => { _interruptMode = 2; Tick(8); };

        // Misc
        _edOps[0x44] = NEG;
        _edOps[0x45] = RETN;
        _edOps[0x4D] = RETI;
        // NEG aliases
        _edOps[0x4C] = NEG;
        _edOps[0x54] = NEG;
        _edOps[0x5C] = NEG;
        _edOps[0x64] = NEG;
        _edOps[0x6C] = NEG;
        _edOps[0x74] = NEG;
        _edOps[0x7C] = NEG;
        // RETN aliases
        _edOps[0x55] = RETN;
        _edOps[0x5D] = RETN;
        _edOps[0x65] = RETN;
        _edOps[0x6D] = RETN;
        _edOps[0x75] = RETN;
        _edOps[0x7D] = RETN;

        // Undocumented LD (nn), HL / LD HL, (nn) duplicates
        _edOps[0x63] = () => { WriteWord(FetchWord(), HL); Tick(20); };
        _edOps[0x6B] = () => { HL = ReadWord(FetchWord()); Tick(20); };

        // IN r, (C) / OUT (C), r
        for (int r = 0; r < 8; r++)
        {
            int reg = r;
            _edOps[0x40 | (reg << 3)] = () =>
            {
                byte val = _ports?.In(BC) ?? 0xFF;
                if (reg != 6) SetReg(reg, val);
                FlagS = (val & 0x80) != 0;
                FlagZ = val == 0;
                FlagH = false;
                FlagPV = GetParity(val);
                FlagN = false;
                SetUndocumentedFlags(val);
                WZ = (ushort)(BC + 1);
                Tick(12);
            };
            _edOps[0x41 | (reg << 3)] = () =>
            {
                byte val = reg == 6 ? (byte)0 : GetReg(reg);
                _ports?.Out(BC, val);
                WZ = (ushort)(BC + 1);
                Tick(12);
            };
        }

        // Block I/O
        _edOps[0xB2] = INIR;
        _edOps[0xBA] = INDR;
        _edOps[0xB3] = OTIR;
        _edOps[0xBB] = OTDR;

        // Block Operations
        _edOps[0xA0] = LDI;
        _edOps[0xA1] = CPI;
        _edOps[0xA8] = LDD;
        _edOps[0xA9] = CPD;
        _edOps[0xB0] = LDIR;
        _edOps[0xB1] = CPIR;
        _edOps[0xB8] = LDDR;
        _edOps[0xB9] = CPDR;
        _edOps[0xA2] = INI;
        _edOps[0xAA] = IND;
        _edOps[0xA3] = OUTI;
        _edOps[0xAB] = OUTD;
    }

    private void DoAdc16(ushort val)
    {
        int carry = FlagC ? 1 : 0;
        int res = HL + val + carry;
        ushort oldHl = HL;
        
        FlagN = false;
        FlagH = (((HL & 0x0FFF) + (val & 0x0FFF) + carry) & 0x1000) != 0;
        FlagPV = (((HL ^ res) & (val ^ res)) & 0x8000) != 0;
        FlagC = res > 0xFFFF;
        
        HL = (ushort)(res & 0xFFFF);
        
        FlagZ = HL == 0;
        FlagS = (HL & 0x8000) != 0;
        WZ = (ushort)(oldHl + 1);
        SetUndocumentedFlags((byte)(HL >> 8));
        Tick(15);
    }

    private void DoSbc16(ushort val)
    {
        int carry = FlagC ? 1 : 0;
        int res = HL - val - carry;
        ushort oldHl = HL;
        
        FlagN = true;
        FlagH = (((HL & 0x0FFF) - (val & 0x0FFF) - carry) & 0x1000) != 0;
        FlagPV = (((HL ^ val) & (HL ^ res)) & 0x8000) != 0;
        FlagC = res < 0;
        
        HL = (ushort)(res & 0xFFFF);
        
        FlagZ = HL == 0;
        FlagS = (HL & 0x8000) != 0;
        WZ = (ushort)(oldHl + 1);
        SetUndocumentedFlags((byte)(HL >> 8));
        Tick(15);
    }

    private void LDI()
    {
        byte val = _bus.Read(HL++);
        _bus.Write(DE++, val);
        BC--;
        FlagN = false;
        FlagH = false;
        FlagPV = BC != 0;

        // Undocumented flags: Bit 5 = (A+val) bit 1, Bit 3 = (A+val) bit 3
        byte res = (byte)(A + val);
        F = (byte)((F & ~0x28) | (res & 0x08) | ((res << 4) & 0x20));
        
        Tick(16);
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
        byte val = _bus.Read(HL--);
        _bus.Write(DE--, val);
        BC--;
        FlagN = false;
        FlagH = false;
        FlagPV = BC != 0;

        // Undocumented flags
        byte res = (byte)(A + val);
        F = (byte)((F & ~0x28) | (res & 0x08) | ((res << 4) & 0x20));

        Tick(16);
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
        byte val = _bus.Read(HL++);
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

        Tick(16);
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
        byte val = _bus.Read(HL--);
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

        Tick(16);
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

    private void HandleED()
    {
        byte opcode = Fetch();
        _edOps[opcode]();
    }

    private void NEG()
    {
        byte val = A;
        A = 0;
        SubInternal(val, false);
        Tick(4); // (8 total including prefix fetch)
    }

    private void RETI() { RET(); IFF1 = IFF2; Tick(4); }
    private void RETN() { RET(); IFF1 = IFF2; Tick(4); }

    private void RRD()
    {
        byte mem = _bus.Read(HL);
        byte lowA = (byte)(A & 0x0F);
        A = (byte)((A & 0xF0) | (mem & 0x0F));
        mem = (byte)((lowA << 4) | (mem >> 4));
        _bus.Write(HL, mem);
        FlagH = false;
        FlagN = false;
        SetLogicFlags(A);
        Tick(14); // (18 total)
    }

    private void INI()
    {
        byte val = _ports?.In(BC) ?? 0xFF;
        _bus.Write(HL++, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
        Tick(16);
    }

    private void INIR()
    {
        INI();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void IND()
    {
        byte val = _ports?.In(BC) ?? 0xFF;
        _bus.Write(HL--, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
        Tick(16);
    }

    private void INDR()
    {
        IND();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void OUTI()
    {
        byte val = _bus.Read(HL++);
        _ports?.Out(BC, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
        Tick(16);
    }

    private void OTIR()
    {
        OUTI();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void OUTD()
    {
        byte val = _bus.Read(HL--);
        _ports?.Out(BC, val);
        B--;
        FlagN = true;
        FlagZ = B == 0;
        Tick(16);
    }

    private void OTDR()
    {
        OUTD();
        if (B != 0) { PC -= 2; Tick(5); }
    }

    private void RLD()
    {
        byte mem = _bus.Read(HL);
        byte lowA = (byte)(A & 0x0F);
        A = (byte)((A & 0xF0) | (mem >> 4));
        mem = (byte)((mem << 4) | lowA);
        _bus.Write(HL, mem);
        FlagH = false;
        FlagN = false;
        SetLogicFlags(A);
        Tick(14); // (18 total)
    }
}

