using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CpuZ80.Tests")]

namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private readonly IBus _bus;
    private readonly IPortBus? _ports;
    private readonly Action[] _ops = new Action[256];
    public bool IFF1 { get; set; }
    public bool IFF2 { get; set; }
    private bool _halted;
    private bool _nmiPending;
    private bool _intPending;
    private byte _intDataBus;
    private bool _eiDelay;

    public void TriggerNmi() => _nmiPending = true;
    public void TriggerInt(byte dataBus = 0xFF) { _intPending = true; _intDataBus = dataBus; }

    private enum IndexMode { HL, IX, IY }
    private IndexMode _indexMode = IndexMode.HL;
    private ushort _ix, _iy;
    private ushort _idxAddr;
    private bool _hasIdxAddr;

    // ── Registers ────────────────────────────────────────────────────────────
    public byte A { get; internal set; }
    public byte F { get; internal set; }
    
    // Flag helpers
    public bool FlagC { get => (F & (byte)Z80Flags.Carry) != 0; set => SetFlag(Z80Flags.Carry, value); }
    public bool FlagN { get => (F & (byte)Z80Flags.AddSub) != 0; set => SetFlag(Z80Flags.AddSub, value); }
    public bool FlagPV { get => (F & (byte)Z80Flags.ParityOverflow) != 0; set => SetFlag(Z80Flags.ParityOverflow, value); }
    public bool FlagH { get => (F & (byte)Z80Flags.HalfCarry) != 0; set => SetFlag(Z80Flags.HalfCarry, value); }
    public bool FlagZ { get => (F & (byte)Z80Flags.Zero) != 0; set => SetFlag(Z80Flags.Zero, value); }
    public bool FlagS { get => (F & (byte)Z80Flags.Sign) != 0; set => SetFlag(Z80Flags.Sign, value); }

    private void SetFlag(Z80Flags flag, bool value)
    {
        if (value) F |= (byte)flag;
        else F &= (byte)~flag;
    }
    public byte B { get; internal set; }
    public byte C { get; internal set; }
    public byte D { get; internal set; }
    public byte E { get; internal set; }
    public byte H { get; internal set; }
    public byte L { get; internal set; }

    // Alternate registers
    private byte A_, F_, B_, C_, D_, E_, H_, L_;

    public ushort BC { get => (ushort)((B << 8) | C); set { B = (byte)(value >> 8); C = (byte)value; } }
    public ushort DE { get => (ushort)((D << 8) | E); set { D = (byte)(value >> 8); E = (byte)value; } }
    public ushort HL { get => (ushort)((H << 8) | L); set { H = (byte)(value >> 8); L = (byte)value; } }

    public ushort WZ;

    public ushort PC { get; internal set; }
    public ushort SP { get; internal set; }

    public ulong TotalCycles { get; private set; }

    private void Tick(int count)
    {
        TotalCycles += (ulong)count;
    }

    public Cpu(IBus bus, IPortBus? ports = null)
    {
        _bus = bus;
        _ports = ports;
        BuildDispatchTable();
        BuildCbDispatchTable();
        BuildEdDispatchTable();
    }

    public void Reset()
    {
        PC   = 0x0000;
        SP   = 0xFFFF;
        IFF1 = false;
        IFF2 = false;
        _halted     = false;
        _nmiPending = false;
        _intPending = false;
        _eiDelay    = false;
        _indexMode  = IndexMode.HL;
        WZ          = 0;
    }

    private void BuildDispatchTable()
    {
        for (int i = 0; i < 256; i++)
        {
            _ops[i] = () => throw new NotImplementedException($"Opcode 0x{_bus.Read((ushort)(PC - 1)):X2} at 0x{(PC - 1):X4} not implemented");
        }

        _ops[0x00] = NOP;
        _ops[0x08] = EX_AF_AF;
        _ops[0xCB] = HandleCB;
        _ops[0xDD] = HandleDD;
        _ops[0xED] = HandleED;
        _ops[0xFD] = HandleFD;
        _ops[0xD9] = EXX;
        _ops[0xEB] = () => { (DE, HL) = (HL, DE); Tick(4); };
        _ops[0xE3] = () => { ushort tmp = GetReg16(2); SetReg16(2, ReadWord(SP)); WriteWord(SP, tmp); Tick(19); };

        // Interrupts
        _ops[0xF3] = () => { IFF1 = IFF2 = false; Tick(4); };
        _ops[0xFB] = () => { IFF1 = IFF2 = true; _eiDelay = true; Tick(4); };

        // Misc A
        _ops[0x27] = () => { DAA(); Tick(4); };
        _ops[0x2F] = () => { A = (byte)~A; FlagN = true; FlagH = true; Tick(4); };
        _ops[0x37] = () => { FlagC = true;  FlagN = false; FlagH = false; Tick(4); };
        _ops[0x3F] = () => { FlagH = FlagC; FlagC = !FlagC; FlagN = false; Tick(4); };

        // 8-bit Rotates (Base table)
        _ops[0x07] = () => { RLCA(); Tick(4); };
        _ops[0x0F] = () => { RRCA(); Tick(4); };
        _ops[0x17] = () => { RLA(); Tick(4); };
        _ops[0x1F] = () => { RRA(); Tick(4); };

        // LD dd, nn
        _ops[0x01] = () => { SetReg16(0, FetchWord()); Tick(10); };
        _ops[0x11] = () => { SetReg16(1, FetchWord()); Tick(10); };
        _ops[0x21] = () => { SetReg16(2, FetchWord()); Tick(10); };
        _ops[0x31] = () => { SetReg16(3, FetchWord()); Tick(10); };

        // LD (nn), HL and LD HL, (nn)
        _ops[0x22] = () => { WriteWord(FetchWord(), GetReg16(2)); Tick(16); };
        _ops[0x2A] = () => { SetReg16(2, ReadWord(FetchWord())); Tick(16); };

        // ADD HL, ss
        _ops[0x09] = () => DoAdd16(GetReg16(0));
        _ops[0x19] = () => DoAdd16(GetReg16(1));
        _ops[0x29] = () => DoAdd16(GetReg16(2));
        _ops[0x39] = () => DoAdd16(GetReg16(3));
        _ops[0xF9] = () => { SP = GetReg16(2); Tick(6); };

        // INC/DEC 16-bit
        _ops[0x03] = () => { SetReg16(0, (ushort)(GetReg16(0) + 1)); Tick(6); };
        _ops[0x13] = () => { SetReg16(1, (ushort)(GetReg16(1) + 1)); Tick(6); };
        _ops[0x23] = () => { SetReg16(2, (ushort)(GetReg16(2) + 1)); Tick(6); };
        _ops[0x33] = () => { SetReg16(3, (ushort)(GetReg16(3) + 1)); Tick(6); };
        _ops[0x0B] = () => { SetReg16(0, (ushort)(GetReg16(0) - 1)); Tick(6); };
        _ops[0x1B] = () => { SetReg16(1, (ushort)(GetReg16(1) - 1)); Tick(6); };
        _ops[0x2B] = () => { SetReg16(2, (ushort)(GetReg16(2) - 1)); Tick(6); };
        _ops[0x3B] = () => { SetReg16(3, (ushort)(GetReg16(3) - 1)); Tick(6); };

        // LD A, (nn) / LD (nn), A
        _ops[0x3A] = () => { A = _bus.Read(FetchWord()); Tick(13); };
        _ops[0x32] = () => { _bus.Write(FetchWord(), A); Tick(13); };

        // LD A, (BC/DE)
        _ops[0x0A] = () => { A = _bus.Read(BC); Tick(7); };
        _ops[0x1A] = () => { A = _bus.Read(DE); Tick(7); };
        _ops[0x02] = () => { _bus.Write(BC, A); Tick(7); };
        _ops[0x12] = () => { _bus.Write(DE, A); Tick(7); };
        
        // LD r, n
        _ops[0x06] = () => { SetReg(0, Fetch()); Tick(7); };
        _ops[0x0E] = () => { SetReg(1, Fetch()); Tick(7); };
        _ops[0x16] = () => { SetReg(2, Fetch()); Tick(7); };
        _ops[0x1E] = () => { SetReg(3, Fetch()); Tick(7); };
        _ops[0x26] = () => { SetReg(4, Fetch()); Tick(7); };
        _ops[0x2E] = () => { SetReg(5, Fetch()); Tick(7); };
        _ops[0x36] = () => { SetReg(6, Fetch()); Tick(10); };
        _ops[0x3E] = () => { SetReg(7, Fetch()); Tick(7); };

        // INC r
        for (int r = 0; r < 8; r++)
        {
            int reg = r;
            _ops[0x04 | (r << 3)] = () => { SetReg(reg, DoInc(GetReg(reg))); Tick((reg == 6) ? 11 : 4); };
        }

        // DEC r
        for (int r = 0; r < 8; r++)
        {
            int reg = r;
            _ops[0x05 | (r << 3)] = () => { SetReg(reg, DoDec(GetReg(reg))); Tick((reg == 6) ? 11 : 4); };
        }

        // LD r, r'
        for (int d = 0; d < 8; d++)
        {
            for (int s = 0; s < 8; s++)
            {
                int opcode = 0x40 | (d << 3) | s;
                if (opcode == 0x76) continue; // HALT

                int dest = d;
                int src = s;
                _ops[opcode] = () => { SetReg(dest, GetReg(src)); Tick((dest == 6 || src == 6) ? 7 : 4); };
            }
        }

        // ADD A, r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x80 | s] = () => { DoAdd(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // ADC A, r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x88 | s] = () => { DoAdc(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // SUB r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x90 | s] = () => { DoSub(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // SBC A, r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x98 | s] = () => { DoSbc(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // AND r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xA0 | s] = () => { DoAnd(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // XOR r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xA8 | s] = () => { DoXor(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // OR r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xB0 | s] = () => { DoOr(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // CP r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xB8 | s] = () => { DoCp(GetReg(src)); Tick((src == 6) ? 7 : 4); };
        }

        // Immediate arithmetic
        _ops[0xC6] = () => { DoAdd(Fetch()); Tick(7); };
        _ops[0xCE] = () => { DoAdc(Fetch()); Tick(7); };
        _ops[0xD6] = () => { DoSub(Fetch()); Tick(7); };
        _ops[0xDE] = () => { DoSbc(Fetch()); Tick(7); };
        _ops[0xE6] = () => { DoAnd(Fetch()); Tick(7); };
        _ops[0xEE] = () => { DoXor(Fetch()); Tick(7); };
        _ops[0xF6] = () => { DoOr(Fetch());  Tick(7); };
        _ops[0xFE] = () => { DoCp(Fetch());  Tick(7); };

        // I/O
        _ops[0xD3] = () => { _ports?.Out((ushort)((A << 8) | Fetch()), A); Tick(11); };
        _ops[0xDB] = () => { A = _ports?.In((ushort)((A << 8) | Fetch())) ?? 0xFF; Tick(11); };

        // Stack instructions
        _ops[0xC5] = PUSH_BC;
        _ops[0xD5] = PUSH_DE;
        _ops[0xE5] = PUSH_HL;
        _ops[0xF5] = PUSH_AF;

        _ops[0xC1] = POP_BC;
        _ops[0xD1] = POP_DE;
        _ops[0xE1] = POP_HL;
        _ops[0xF1] = POP_AF;

        // Control Flow
        _ops[0xC3] = JP_nn;
        _ops[0x18] = JR_e;
        _ops[0xCD] = CALL_nn;
        _ops[0xC9] = RET;
        _ops[0xE9] = () => { PC = GetReg16(2); Tick(4); };
        _ops[0x76] = () => { _halted = true; PC--; Tick(4); };
        _ops[0x10] = DJNZ;

        // RST
        for (int t = 0; t < 8; t++)
        {
            int vec = t;
            _ops[0xC7 | (vec << 3)] = () => { Push(PC); PC = (ushort)(vec << 3); Tick(11); };
        }

        for (int cc = 0; cc < 8; cc++)
        {
            int condition = cc;
            _ops[0xC2 | (cc << 3)] = () => JP_cc_nn(condition);
            _ops[0xC4 | (cc << 3)] = () => CALL_cc_nn(condition);
            _ops[0xC0 | (cc << 3)] = () => RET_cc(condition);
            
            if (cc < 4) // JR only has 4 conditions: NZ, Z, NC, C
            {
                _ops[0x20 | (cc << 3)] = () => JR_cc_e(condition);
            }
        }
    }

    private void NOP() { Tick(4); }

    private void EX_AF_AF()
    {
        (A, A_) = (A_, A);
        (F, F_) = (F_, F);
        Tick(4);
    }

    private void EXX()
    {
        (B, B_) = (B_, B);
        (C, C_) = (C_, C);
        (D, D_) = (D_, D);
        (E, E_) = (E_, E);
        (H, H_) = (H_, H);
        (L, L_) = (L_, L);
        Tick(4);
    }

    public void Step()
    {
        if (_nmiPending)
        {
            _halted = false;
            _nmiPending = false;
            _eiDelay = false;
            AcceptNmi();
            return;
        }
        if (_intPending && IFF1 && !_eiDelay)
        {
            _halted = false;
            _intPending = false;
            AcceptInt();
            return;
        }
        _eiDelay = false;
        if (_halted) { R = (byte)((R & 0x80) | ((R + 1) & 0x7F)); Tick(4); return; }
        
        byte opcode = Fetch();
        
        // Use generator for NOP and ADD A, r
        if (IsGenerated(opcode))
        {
            StepGenerated(opcode);
        }
        else
        {
            _ops[opcode]();
        }
    }

    private byte Fetch()
    {
        R = (byte)((R & 0x80) | ((R + 1) & 0x7F)); // Bit 7 is preserved, bits 0-6 increment
        return _bus.Read(PC++);
    }

    private ushort FetchWord()
    {
        byte lo = Fetch();
        byte hi = Fetch();
        return (ushort)((hi << 8) | lo);
    }

    private ushort ReadWord(ushort addr)
    {
        byte lo = _bus.Read(addr);
        byte hi = _bus.Read((ushort)(addr + 1));
        return (ushort)((hi << 8) | lo);
    }

    private void WriteWord(ushort addr, ushort val)
    {
        _bus.Write(addr, (byte)(val & 0xFF));
        _bus.Write((ushort)(addr + 1), (byte)(val >> 8));
    }

    private byte GetReg(int index)
    {
        if (_indexMode != IndexMode.HL)
        {
            ushort reg = _indexMode == IndexMode.IX ? _ix : _iy;
            if (index == 4) return (byte)(reg >> 8);
            if (index == 5) return (byte)(reg & 0xFF);
            if (index == 6)
            {
                sbyte d = (sbyte)Fetch();
                _idxAddr = (ushort)(reg + d);
                _hasIdxAddr = true;
                Tick(8);
                return _bus.Read(_idxAddr);
            }
        }

        return index switch
        {
            0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L, 6 => _bus.Read(HL), 7 => A,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    private ushort GetReg16(int index) // 0=BC, 1=DE, 2=HL/IX/IY, 3=SP
    {
        return index switch
        {
            0 => BC,
            1 => DE,
            2 => _indexMode == IndexMode.IX ? _ix : (_indexMode == IndexMode.IY ? _iy : HL),
            3 => SP,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    private void SetReg16(int index, ushort val)
    {
        switch (index)
        {
            case 0: BC = val; break;
            case 1: DE = val; break;
            case 2:
                if (_indexMode == IndexMode.IX) _ix = val;
                else if (_indexMode == IndexMode.IY) _iy = val;
                else HL = val;
                break;
            case 3: SP = val; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private void SetReg(int index, byte val)
    {
        if (_indexMode != IndexMode.HL)
        {
            if (index == 4)
            {
                if (_indexMode == IndexMode.IX) _ix = (ushort)((_ix & 0x00FF) | (val << 8));
                else                            _iy = (ushort)((_iy & 0x00FF) | (val << 8));
                return;
            }
            if (index == 5)
            {
                if (_indexMode == IndexMode.IX) _ix = (ushort)((_ix & 0xFF00) | val);
                else                            _iy = (ushort)((_iy & 0xFF00) | val);
                return;
            }
            if (index == 6)
            {
                ushort addr;
                if (_hasIdxAddr)
                {
                    addr = _idxAddr;
                    _hasIdxAddr = false;
                }
                else
                {
                    ushort reg = _indexMode == IndexMode.IX ? _ix : _iy;
                    sbyte d = (sbyte)Fetch();
                    addr = (ushort)(reg + d);
                    Tick(8);
                }
                _bus.Write(addr, val);
                return;
            }
        }

        switch (index)
        {
            case 0: B = val; break;
            case 1: C = val; break;
            case 2: D = val; break;
            case 3: E = val; break;
            case 4: H = val; break;
            case 5: L = val; break;
            case 6: _bus.Write(HL, val); break;
            case 7: A = val; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

