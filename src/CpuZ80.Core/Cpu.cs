using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CpuZ80.Tests")]

namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private readonly IBus _bus;
    private readonly IPortBus? _ports;
    private readonly Action[] _ops = new Action[256];
    private bool _iff1, _iff2;

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

    public ushort PC { get; internal set; }
    public ushort SP { get; internal set; }

    public ulong TotalCycles { get; private set; }

    public Cpu(IBus bus, IPortBus? ports = null)
    {
        _bus = bus;
        _ports = ports;
        BuildDispatchTable();
        BuildCbDispatchTable();
        BuildEdDispatchTable();
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
        _ops[0xEB] = () => { (DE, HL) = (HL, DE); TotalCycles += 4UL; };
        _ops[0xE3] = () => { ushort tmp = HL; HL = ReadWord(SP); WriteWord(SP, tmp); TotalCycles += 19UL; };

        // Interrupts
        _ops[0xF3] = () => { _iff1 = _iff2 = false; TotalCycles += 4UL; };
        _ops[0xFB] = () => { _iff1 = _iff2 = true;  TotalCycles += 4UL; };

        // Misc A
        _ops[0x27] = DAA;
        _ops[0x2F] = () => { A = (byte)~A; FlagN = true; FlagH = true; TotalCycles += 4UL; };
        _ops[0x37] = () => { FlagC = true;  FlagN = false; FlagH = false; TotalCycles += 4UL; };
        _ops[0x3F] = () => { FlagH = FlagC; FlagC = !FlagC; FlagN = false; TotalCycles += 4UL; };

        // 8-bit Rotates (Base table)
        _ops[0x07] = RLCA;
        _ops[0x0F] = RRCA;
        _ops[0x17] = RLA;
        _ops[0x1F] = RRA;

        // LD dd, nn
        _ops[0x01] = () => { BC = FetchWord(); TotalCycles += 10UL; };
        _ops[0x11] = () => { DE = FetchWord(); TotalCycles += 10UL; };
        _ops[0x21] = () => { HL = FetchWord(); TotalCycles += 10UL; };
        _ops[0x31] = () => { SP = FetchWord(); TotalCycles += 10UL; };

        // LD (nn), HL and LD HL, (nn)
        _ops[0x22] = () => { WriteWord(FetchWord(), HL); TotalCycles += 16UL; };
        _ops[0x2A] = () => { HL = ReadWord(FetchWord()); TotalCycles += 16UL; };

        // ADD HL, ss
        _ops[0x09] = () => DoAdd16(BC);
        _ops[0x19] = () => DoAdd16(DE);
        _ops[0x29] = () => DoAdd16(HL);
        _ops[0x39] = () => DoAdd16(SP);
        _ops[0xF9] = () => { SP = HL; TotalCycles += 6UL; };

        // INC/DEC 16-bit
        _ops[0x03] = () => { BC++; TotalCycles += 6UL; };
        _ops[0x13] = () => { DE++; TotalCycles += 6UL; };
        _ops[0x23] = () => { HL++; TotalCycles += 6UL; };
        _ops[0x33] = () => { SP++; TotalCycles += 6UL; };
        _ops[0x0B] = () => { BC--; TotalCycles += 6UL; };
        _ops[0x1B] = () => { DE--; TotalCycles += 6UL; };
        _ops[0x2B] = () => { HL--; TotalCycles += 6UL; };
        _ops[0x3B] = () => { SP--; TotalCycles += 6UL; };

        // LD A, (nn) / LD (nn), A
        _ops[0x3A] = () => { A = _bus.Read(FetchWord()); TotalCycles += 13UL; };
        _ops[0x32] = () => { _bus.Write(FetchWord(), A); TotalCycles += 13UL; };

        // LD A, (BC/DE)
        _ops[0x0A] = () => { A = _bus.Read(BC); TotalCycles += 7UL; };
        _ops[0x1A] = () => { A = _bus.Read(DE); TotalCycles += 7UL; };
        _ops[0x02] = () => { _bus.Write(BC, A); TotalCycles += 7UL; };
        _ops[0x12] = () => { _bus.Write(DE, A); TotalCycles += 7UL; };
        
        // LD r, n
        _ops[0x06] = () => { B = Fetch(); TotalCycles += 7UL; };
        _ops[0x0E] = () => { C = Fetch(); TotalCycles += 7UL; };
        _ops[0x16] = () => { D = Fetch(); TotalCycles += 7UL; };
        _ops[0x1E] = () => { E = Fetch(); TotalCycles += 7UL; };
        _ops[0x26] = () => { H = Fetch(); TotalCycles += 7UL; };
        _ops[0x2E] = () => { L = Fetch(); TotalCycles += 7UL; };
        _ops[0x36] = () => { _bus.Write(HL, Fetch()); TotalCycles += 10UL; };
        _ops[0x3E] = () => { A = Fetch(); TotalCycles += 7UL; };

        // INC r
        for (int r = 0; r < 8; r++)
        {
            int reg = r;
            _ops[0x04 | (r << 3)] = () => { SetReg(reg, DoInc(GetReg(reg))); TotalCycles += (reg == 6) ? 11UL : 4UL; };
        }

        // DEC r
        for (int r = 0; r < 8; r++)
        {
            int reg = r;
            _ops[0x05 | (r << 3)] = () => { SetReg(reg, DoDec(GetReg(reg))); TotalCycles += (reg == 6) ? 11UL : 4UL; };
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
                _ops[opcode] = () => { SetReg(dest, GetReg(src)); TotalCycles += (dest == 6 || src == 6) ? 7UL : 4UL; };
            }
        }

        // ADD A, r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x80 | s] = () => { DoAdd(GetReg(src)); if (src == 6) TotalCycles += 3UL; }; // (HL) takes 7 cycles total
        }

        // ADC A, r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x88 | s] = () => { DoAdc(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // SUB r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x90 | s] = () => { DoSub(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // SBC A, r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0x98 | s] = () => { DoSbc(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // AND r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xA0 | s] = () => { DoAnd(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // XOR r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xA8 | s] = () => { DoXor(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // OR r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xB0 | s] = () => { DoOr(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // CP r
        for (int s = 0; s < 8; s++)
        {
            int src = s;
            _ops[0xB8 | s] = () => { DoCp(GetReg(src)); if (src == 6) TotalCycles += 3UL; };
        }

        // Immediate arithmetic
        _ops[0xC6] = () => { DoAdd(Fetch()); TotalCycles += 3UL; }; // ADD A, n (4+3=7)
        _ops[0xCE] = () => { DoAdc(Fetch()); TotalCycles += 3UL; }; // ADC A, n (4+3=7)
        _ops[0xD6] = () => { DoSub(Fetch()); TotalCycles += 3UL; }; // SUB n
        _ops[0xDE] = () => { DoSbc(Fetch()); TotalCycles += 3UL; }; // SBC A, n
        _ops[0xE6] = () => { DoAnd(Fetch()); TotalCycles += 3UL; }; // AND n
        _ops[0xEE] = () => { DoXor(Fetch()); TotalCycles += 3UL; }; // XOR n
        _ops[0xF6] = () => { DoOr(Fetch());  TotalCycles += 3UL; }; // OR n
        _ops[0xFE] = () => { DoCp(Fetch());  TotalCycles += 3UL; }; // CP n

        // I/O
        _ops[0xD3] = () => { _ports?.Out((ushort)((A << 8) | Fetch()), A); TotalCycles += 11UL; };
        _ops[0xDB] = () => { A = _ports?.In((ushort)((A << 8) | Fetch())) ?? 0xFF; TotalCycles += 11UL; };

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

    private void NOP() { TotalCycles += 4UL; }

    private void EX_AF_AF()
    {
        (A, A_) = (A_, A);
        (F, F_) = (F_, F);
        TotalCycles += 4UL;
    }

    private void EXX()
    {
        (B, B_) = (B_, B);
        (C, C_) = (C_, C);
        (D, D_) = (D_, D);
        (E, E_) = (E_, E);
        (H, H_) = (H_, H);
        (L, L_) = (L_, L);
        TotalCycles += 4UL;
    }

    public void Step()
    {
        byte opcode = Fetch();
        _ops[opcode]();
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

    private byte GetReg(int index) => index switch
    {
        0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L, 6 => _bus.Read(HL), 7 => A,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private void SetReg(int index, byte val)
    {
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
