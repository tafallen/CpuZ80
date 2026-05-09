using System.Text;

namespace CpuZ80.CodeGen;

public record Instruction(byte Opcode, string Mnemonic, string[] Actions, int[] Cycles)
{
    public Instruction(byte opcode, string mnemonic, string action, int[] cycles, string? wzAction = null)
        : this(opcode, mnemonic, new[] { action }, cycles)
    {
        if (!string.IsNullOrEmpty(wzAction)) {
            Actions[0] = Actions[0] + (string.IsNullOrEmpty(Actions[0]) ? "" : "; ") + wzAction;
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        string outputPath = args.Length > 0 ? args[0] : "../CpuZ80.Core/Cpu.Generated.cs";
        
        var baseInstructions = new List<Instruction>();
        var cbInstructions = new List<Instruction>();
        var edInstructions = new List<Instruction>();

        // 8-bit registers for patterns
        string[] regs = { "B", "C", "D", "E", "H", "L", "_bus.Read(HL)", "A" };
        string[] regSetters = { "B = {0}", "C = {0}", "D = {0}", "E = {0}", "H = {0}", "L = {0}", "SetReg(6, {0})", "A = {0}" };

        // --- Base Instructions ---
        baseInstructions.Add(new Instruction(0x00, "NOP", "", new[] { 4 }));

        string[] dd = { "BC", "DE", "HL", "SP" };
        for (int i = 0; i < 4; i++) {
            string reg = dd[i];
            baseInstructions.Add(new Instruction((byte)(0x01 | (i << 4)), $"LD {reg}, nn", 
                new[] { "", "byte lo = Fetch()", $"byte hi = Fetch(); {reg} = (ushort)(lo | (hi << 8))" }, 
                new[] { 4, 3, 3 }));
        }

        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x03 | (i << 4)), $"INC {dd[i]}", $"{dd[i]}++", new[] { 6 }));
        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x0B | (i << 4)), $"DEC {dd[i]}", $"{dd[i]}--", new[] { 6 }));
        
        for (int i = 0; i < 8; i++) {
            if (i == 6) {
                 baseInstructions.Add(new Instruction((byte)(0x06 | (i << 3)), $"LD {regs[i]}, n", 
                    new[] { "", "byte n = Fetch()", "SetReg(6, n)" }, 
                    new[] { 4, 3, 3 }));
            } else {
                 baseInstructions.Add(new Instruction((byte)(0x06 | (i << 3)), $"LD {regs[i]}, n", 
                    new[] { "", $"byte n = Fetch(); {regs[i]} = n" }, 
                    new[] { 4, 3 }));
            }
        }

        baseInstructions.Add(new Instruction(0x02, "LD (BC), A", new[] { "", "_bus.Write(BC, A)" }, new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x12, "LD (DE), A", new[] { "", "_bus.Write(DE, A)" }, new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x0A, "LD A, (BC)", new[] { "", "A = _bus.Read(BC); WZ = (ushort)(BC + 1)" }, new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x1A, "LD A, (DE)", new[] { "", "A = _bus.Read(DE); WZ = (ushort)(DE + 1)" }, new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x22, "LD (nn), HL", 
            new[] { "", "byte lo = Fetch()", "byte hi = Fetch(); ushort nn = (ushort)(lo | (hi << 8)); WZ = (ushort)(nn + 1)", "_bus.Write(nn, L)", "_bus.Write((ushort)(nn + 1), H)" }, 
            new[] { 4, 3, 3, 3, 3 }));
        baseInstructions.Add(new Instruction(0x2A, "LD HL, (nn)", 
            new[] { "", "byte lo = Fetch()", "byte hi = Fetch(); ushort nn = (ushort)(lo | (hi << 8)); WZ = (ushort)(nn + 1)", "L = _bus.Read(nn)", "H = _bus.Read((ushort)(nn + 1))" }, 
            new[] { 4, 3, 3, 3, 3 }));
        baseInstructions.Add(new Instruction(0x32, "LD (nn), A", 
            new[] { "", "byte lo = Fetch()", "byte hi = Fetch(); ushort nn = (ushort)(lo | (hi << 8)); WZ = (ushort)((A << 8) | ((nn + 1) & 0xFF))", "_bus.Write(nn, A)" }, 
            new[] { 4, 3, 3, 3 }));
        baseInstructions.Add(new Instruction(0x3A, "LD A, (nn)", 
            new[] { "", "byte lo = Fetch()", "byte hi = Fetch(); ushort nn = (ushort)(lo | (hi << 8)); WZ = (ushort)(nn + 1)", "A = _bus.Read(nn)" }, 
            new[] { 4, 3, 3, 3 }));

        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x09 | (i << 4)), $"ADD HL, {dd[i]}", $"HL = DoAdd16(HL, {dd[i]})", new[] { 4, 4, 3 })); 
        for (int i = 0; i < 8; i++) baseInstructions.Add(new Instruction((byte)(0x04 | (i << 3)), $"INC {regs[i]}", string.Format(regSetters[i], $"DoInc({regs[i]})"), (i == 6 ? new[] { 4, 3, 4 } : new[] { 4 })));
        for (int i = 0; i < 8; i++) baseInstructions.Add(new Instruction((byte)(0x05 | (i << 3)), $"DEC {regs[i]}", string.Format(regSetters[i], $"DoDec({regs[i]})"), (i == 6 ? new[] { 4, 3, 4 } : new[] { 4 })));

        for (int d = 0; d < 8; d++)
            for (int s = 0; s < 8; s++) {
                int opcode = 0x40 | (d << 3) | s;
                if (opcode == 0x76) continue;
                baseInstructions.Add(new Instruction((byte)opcode, $"LD {regs[d]}, {regs[s]}", string.Format(regSetters[d], regs[s]), (d == 6 || s == 6 ? new[] { 4, 3 } : new[] { 4 })));
            }

        string[] aluOps = { "DoAdd", "DoAdc", "DoSub", "DoSbc", "DoAnd", "DoXor", "DoOr", "DoCp" };
        string[] aluNames = { "ADD A,", "ADC A,", "SUB", "SBC A,", "AND", "XOR", "OR", "CP" };
        for (int op = 0; op < 8; op++)
            for (int s = 0; s < 8; s++)
                baseInstructions.Add(new Instruction((byte)(0x80 | (op << 3) | s), $"{aluNames[op]} {regs[s]}", $"{aluOps[op]}({regs[s]})", (s == 6 ? new[] { 4, 3 } : new[] { 4 })));

        for (int op = 0; op < 8; op++) baseInstructions.Add(new Instruction((byte)(0xC6 | (op << 3)), $"{aluNames[op]} n", $"{aluOps[op]}(Fetch())", new[] { 4, 3 }));

        string[] conditions = { "!FlagZ", "FlagZ", "!FlagC", "FlagC", "!FlagPV", "FlagPV", "!FlagS", "FlagS" };
        string[] condNames = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
        for (int cc = 0; cc < 8; cc++) {
            baseInstructions.Add(new Instruction((byte)(0xC0 | (cc << 3)), $"RET {condNames[cc]}", 
                new[] { "if (!" + conditions[cc] + ") return", "PC = Pop()" }, new[] { 5, 6 })); // 5, 11 total
            baseInstructions.Add(new Instruction((byte)(0xC2 | (cc << 3)), $"JP {condNames[cc]}, nn", 
                new[] { "", "byte lo = Fetch()", "byte hi = Fetch(); if (" + conditions[cc] + ") PC = (ushort)(lo | (hi << 8))" }, new[] { 4, 3, 3 }));
            baseInstructions.Add(new Instruction((byte)(0xC4 | (cc << 3)), $"CALL {condNames[cc]}, nn", 
                new[] { "", "byte lo = Fetch()", "byte hi = Fetch(); if (" + conditions[cc] + ") { ushort nn = (ushort)(lo | (hi << 8)); Push(PC); PC = nn; Tick(7); }" }, new[] { 4, 3, 3 }));
            if (cc < 4) baseInstructions.Add(new Instruction((byte)(0x20 | (cc << 3)), $"JR {condNames[cc]}, e", 
                new[] { "", "sbyte e = (sbyte)Fetch(); if (" + conditions[cc] + ") { PC = (ushort)(PC + e); Tick(5); }" }, new[] { 4, 3 }));
        }

        string[] qq = { "BC", "DE", "HL", "AF" };
        for (int i = 0; i < 4; i++) {
            string reg = qq[i];
            string val = (reg == "AF") ? "((A << 8) | F)" : reg;
            
            // PUSH: M1(5) fetch+internal, M2(3) write hi, M3(3) write lo. Total 11.
            baseInstructions.Add(new Instruction((byte)(0xC5 | (i << 4)), $"PUSH {reg}", 
                new[] { "ushort v = (ushort)(" + val + ")", "SP--; _bus.Write(SP, (byte)(v >> 8))", "SP--; _bus.Write(SP, (byte)(v & 0xFF))" }, 
                new[] { 5, 3, 3 }));

            string popSet = (reg == "AF") ? "A = hi; F = lo" : reg + " = (ushort)((hi << 8) | lo)";
            
            // POP: M1(4), M2(3) read lo, M3(3) read hi. Total 10.
            baseInstructions.Add(new Instruction((byte)(0xC1 | (i << 4)), $"POP {reg}", 
                new[] { "", "byte lo = _bus.Read(SP); SP++", "byte hi = _bus.Read(SP); SP++; " + popSet }, 
                new[] { 4, 3, 3 }));
        }

        for (int t = 0; t < 8; t++) baseInstructions.Add(new Instruction((byte)(0xC7 | (t << 3)), $"RST {t * 8:X2}h", $"{{ Push(PC); PC = 0x{t * 8:X2}; }}", new[] { 4, 3, 4 }));

        baseInstructions.Add(new Instruction(0x07, "RLCA", "RLCA()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x0F, "RRCA", "RRCA()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x17, "RLA", "RLA()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x1F, "RRA", "RRA()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xC3, "JP nn", "PC = FetchWord()", new[] { 4, 3, 3 }));
        baseInstructions.Add(new Instruction(0x18, "JR e", "{ sbyte e = (sbyte)Fetch(); PC = (ushort)(PC + e); }", new[] { 4, 3, 5 }));
        baseInstructions.Add(new Instruction(0xCD, "CALL nn", "{ ushort nn = FetchWord(); Push(PC); PC = nn; }", new[] { 4, 3, 3, 3, 4 }));
        baseInstructions.Add(new Instruction(0xC9, "RET", "PC = Pop()", new[] { 4, 3, 3 }));
        baseInstructions.Add(new Instruction(0x08, "EX AF, AF'", "EX_AF_AF()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xD9, "EXX", "EXX()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xEB, "EX DE, HL", "(DE, HL) = (HL, DE)", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xE3, "EX (SP), HL", "{ ushort tmp = HL; HL = ReadWord(SP); WriteWord(SP, tmp); }", new[] { 4, 3, 4, 3, 5 }));
        baseInstructions.Add(new Instruction(0xF3, "DI", "IFF1 = IFF2 = false", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xFB, "EI", "IFF1 = IFF2 = true; _eiDelay = true;", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x27, "DAA", "DAA()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x2F, "CPL", "A = (byte)~A; FlagN = true; FlagH = true; SetUndocumentedFlags(A);", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x37, "SCF", "FlagC = true; FlagN = false; FlagH = false; SetUndocumentedFlags(A);", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x3F, "CCF", "FlagH = FlagC; FlagC = !FlagC; FlagN = false; SetUndocumentedFlags(A);", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xF9, "LD SP, HL", "SP = HL", new[] { 6 }));
        baseInstructions.Add(new Instruction(0xE9, "JP (HL)", "PC = HL", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x76, "HALT", "{ _halted = true; PC--; }", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x10, "DJNZ e", "{ sbyte e = (sbyte)Fetch(); B--; if (B != 0) { PC = (ushort)(PC + e); WZ = PC; Tick(5); } }", new[] { 4, 4 }));
        baseInstructions.Add(new Instruction(0xD3, "OUT (n), A", "{ byte n = Fetch(); _ports?.Out((ushort)((A << 8) | n), A); WZ = (ushort)((A << 8) | ((n + 1) & 0xFF)); }", new[] { 4, 3, 4 }));
        baseInstructions.Add(new Instruction(0xDB, "IN A, (n)", "{ byte n = Fetch(); ushort port = (ushort)((A << 8) | n); A = _ports?.In(port) ?? 0xFF; WZ = (ushort)(port + 1); }", new[] { 4, 3, 4 }));

        // --- CB Instructions ---
        string[] shiftNames = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };
        for (int op = 0; op < 8; op++)
            for (int s = 0; s < 8; s++)
                cbInstructions.Add(new Instruction((byte)(op << 3 | s), $"{shiftNames[op]} {regs[s]}", string.Format(regSetters[s], $"DoShift({op}, {regs[s]})"), (s == 6 ? new[] { 4, 3, 4 } : new[] { 4 })));

        for (int bit = 0; bit < 8; bit++)
            for (int s = 0; s < 8; s++) {
                string bitAction = $"DoBit({bit}, {regs[s]})";
                if (s == 6) bitAction = $"{{ WZ = HL; {bitAction}; SetUndocumentedFlagsFromWZ(); }}";
                cbInstructions.Add(new Instruction((byte)(0x40 | (bit << 3) | s), $"BIT {bit}, {regs[s]}", bitAction, (s == 6 ? new[] { 4, 4 } : new[] { 4 })));
                cbInstructions.Add(new Instruction((byte)(0x80 | (bit << 3) | s), $"RES {bit}, {regs[s]}", string.Format(regSetters[s], $" (byte)({regs[s]} & ~(1 << {bit}))"), (s == 6 ? new[] { 4, 3, 4 } : new[] { 4 })));
                cbInstructions.Add(new Instruction((byte)(0xC0 | (bit << 3) | s), $"SET {bit}, {regs[s]}", string.Format(regSetters[s], $" (byte)({regs[s]} | (1 << {bit}))"), (s == 6 ? new[] { 4, 3, 4 } : new[] { 4 })));
            }

        // --- ED Instructions ---
        for (int i = 0; i < 4; i++) {
            edInstructions.Add(new Instruction((byte)(0x4A | (i << 4)), $"ADC HL, {dd[i]}", 
                new[] { "", "HL = DoAdc16(HL, " + dd[i] + ")" }, new[] { 4, 4, 3 })); // 15 total (prefix excluded)
            edInstructions.Add(new Instruction((byte)(0x42 | (i << 4)), $"SBC HL, {dd[i]}", 
                new[] { "", "HL = DoSbc16(HL, " + dd[i] + ")" }, new[] { 4, 4, 3 })); // 15 total
        }
        edInstructions.Add(new Instruction(0x67, "RRD", 
            new[] { "", "byte tmp = _bus.Read(HL); _bus.Write(HL, (byte)((tmp >> 4) | (A << 4))); A = (byte)((A & 0xF0) | (tmp & 0x0F)); SetLogicFlags(A); WZ = (ushort)(HL + 1)" }, 
            new[] { 4, 3, 4, 3 })); // 18 total
        edInstructions.Add(new Instruction(0x6F, "RLD", 
            new[] { "", "byte tmp = _bus.Read(HL); _bus.Write(HL, (byte)((tmp << 4) | (A & 0x0F))); A = (byte)((A & 0xF0) | (tmp >> 4)); SetLogicFlags(A); WZ = (ushort)(HL + 1)" }, 
            new[] { 4, 3, 4, 3 })); // 18 total
        for (int i = 0; i < 3; i++) {
            if (i == 2) continue; // SP handle separately
            edInstructions.Add(new Instruction((byte)(0x4B | (i << 4)), $"LD {dd[i]}, (nn)", 
                new[] { "byte lo = Fetch()", "byte hi = Fetch(); ushort nn = (ushort)(lo | (hi << 8)); WZ = (ushort)(nn + 1)", $"byte valLo = _bus.Read(nn)", $"byte valHi = _bus.Read((ushort)(nn + 1)); {dd[i]} = (ushort)(valLo | (valHi << 8))" }, 
                new[] { 4, 3, 3, 3, 3 })); // 20 total
            edInstructions.Add(new Instruction((byte)(0x43 | (i << 4)), $"LD (nn), {dd[i]}", 
                new[] { "byte lo = Fetch()", "byte hi = Fetch(); ushort nn = (ushort)(lo | (hi << 8)); WZ = (ushort)(nn + 1)", $"_bus.Write(nn, (byte)({dd[i]} & 0xFF))", $"_bus.Write((ushort)(nn + 1), (byte)({dd[i]} >> 8))" }, 
                new[] { 4, 3, 3, 3, 3 }));
        }
        edInstructions.Add(new Instruction(0x7B, "LD SP, (nn)", "SP = ReadWord(FetchWord())", new[] { 4, 3, 3, 3, 3 }));
        edInstructions.Add(new Instruction(0x73, "LD (nn), SP", "WriteWord(FetchWord(), SP)", new[] { 4, 3, 3, 3, 3 }));

        // Undocumented LD (nn), HL duplicates
        edInstructions.Add(new Instruction(0x63, "LD (nn), HL", "WriteWord(FetchWord(), HL)", new[] { 4, 3, 3, 3, 3 }));
        edInstructions.Add(new Instruction(0x6B, "LD HL, (nn)", "HL = ReadWord(FetchWord())", new[] { 4, 3, 3, 3, 3 }));

        edInstructions.Add(new Instruction(0x47, "LD I, A", new[] { "I = A" }, new[] { 5 })); // 9 total
        edInstructions.Add(new Instruction(0x4F, "LD R, A", new[] { "R = A" }, new[] { 5 }));
        edInstructions.Add(new Instruction(0x57, "LD A, I", new[] { "A = I; SetLogicFlags(A); FlagPV = IFF2; FlagN = false; FlagH = false" }, new[] { 5 }));
        edInstructions.Add(new Instruction(0x5F, "LD A, R", new[] { "A = R; SetLogicFlags(A); FlagPV = IFF2; FlagN = false; FlagH = false" }, new[] { 5 }));

        edInstructions.Add(new Instruction(0x46, "IM 0", new[] { "_interruptMode = 0" }, new[] { 4 }));
        edInstructions.Add(new Instruction(0x56, "IM 1", new[] { "_interruptMode = 1" }, new[] { 4 }));
        edInstructions.Add(new Instruction(0x5E, "IM 2", new[] { "_interruptMode = 2" }, new[] { 4 }));
        // IM aliases
        foreach (byte op in new byte[] { 0x4E, 0x66, 0x6E }) edInstructions.Add(new Instruction(op, "IM 0", new[] { "_interruptMode = 0" }, new[] { 4 }));
        foreach (byte op in new byte[] { 0x76 }) edInstructions.Add(new Instruction(op, "IM 1", new[] { "_interruptMode = 1" }, new[] { 4 }));
        foreach (byte op in new byte[] { 0x7E }) edInstructions.Add(new Instruction(op, "IM 2", new[] { "_interruptMode = 2" }, new[] { 4 }));

        edInstructions.Add(new Instruction(0x44, "NEG", new[] { "NEG()" }, new[] { 4 })); // 8 total
        // NEG aliases
        for (byte op = 0x4C; op <= 0x7C; op += 0x08) edInstructions.Add(new Instruction(op, "NEG", new[] { "NEG()" }, new[] { 4 }));
        foreach (byte op in new byte[] { 0x54, 0x64, 0x74 }) edInstructions.Add(new Instruction(op, "NEG", new[] { "NEG()" }, new[] { 4 }));

        edInstructions.Add(new Instruction(0x4D, "RETI", new[] { "RETI()" }, new[] { 4, 3, 3 })); // 14 total
        edInstructions.Add(new Instruction(0x45, "RETN", new[] { "RETN()" }, new[] { 4, 3, 3 })); // 14 total
        // RETN aliases
        for (byte op = 0x55; op <= 0x7D; op += 0x08) edInstructions.Add(new Instruction(op, "RETN", new[] { "RETN()" }, new[] { 4, 3, 3 }));
        foreach (byte op in new byte[] { 0x5D, 0x6D, 0x7D }) edInstructions.Add(new Instruction(op, "RETN", new[] { "RETN()" }, new[] { 4, 3, 3 }));

        for (int r = 0; r < 8; r++) {
            edInstructions.Add(new Instruction((byte)(0x40 | (r << 3)), $"IN {regs[r]}, (C)", 
                new[] { "", "{ byte val = _ports?.In(BC) ?? 0xFF; if (" + r + " != 6) " + string.Format(regSetters[r], "val") + "; FlagS = (val & 0x80) != 0; FlagZ = val == 0; FlagH = false; FlagPV = GetParity(val); FlagN = false; SetUndocumentedFlags(val); }" }, 
                new[] { 4, 4 })); // 12 total
            edInstructions.Add(new Instruction((byte)(0x41 | (r << 3)), $"OUT (C), {regs[r]}", 
                new[] { "", "{ byte val = " + (r == 6 ? "(byte)0" : regs[r]) + "; _ports?.Out(BC, val); }" }, 
                new[] { 4, 4 })); // 12 total
        }

        edInstructions.Add(new Instruction(0xA0, "LDI", new[] { "", "LDI()" }, new[] { 4, 3, 3, 2 })); // 16 total
        edInstructions.Add(new Instruction(0xA1, "CPI", new[] { "", "CPI()" }, new[] { 4, 3, 5 })); // 16 total
        edInstructions.Add(new Instruction(0xA8, "LDD", new[] { "", "LDD()" }, new[] { 4, 3, 3, 2 })); 
        edInstructions.Add(new Instruction(0xA9, "CPD", new[] { "", "CPD()" }, new[] { 4, 3, 5 }));
        
        edInstructions.Add(new Instruction(0xB0, "LDIR", new[] { "", "LDI(); if (BC != 0) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 3, 2 }));
        edInstructions.Add(new Instruction(0xB1, "CPIR", new[] { "", "CPI(); if (BC != 0 && !FlagZ) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 5 }));
        edInstructions.Add(new Instruction(0xB8, "LDDR", new[] { "", "LDD(); if (BC != 0) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 3, 2 }));
        edInstructions.Add(new Instruction(0xB9, "CPDR", new[] { "", "CPD(); if (BC != 0 && !FlagZ) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 5 }));

        edInstructions.Add(new Instruction(0xA2, "INI", new[] { "", "INI()" }, new[] { 4, 3, 5 })); 
        edInstructions.Add(new Instruction(0xAA, "IND", new[] { "", "IND()" }, new[] { 4, 3, 5 }));
        edInstructions.Add(new Instruction(0xA3, "OUTI", new[] { "", "OUTI()" }, new[] { 4, 3, 5 }));
        edInstructions.Add(new Instruction(0xAB, "OUTD", new[] { "", "OUTD()" }, new[] { 4, 3, 5 }));
        
        edInstructions.Add(new Instruction(0xB2, "INIR", new[] { "", "INI(); if (B != 0) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 5 }));
        edInstructions.Add(new Instruction(0xBA, "INDR", new[] { "", "IND(); if (B != 0) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 5 }));
        edInstructions.Add(new Instruction(0xB3, "OTIR", new[] { "", "OUTI(); if (B != 0) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 5 }));
        edInstructions.Add(new Instruction(0xBB, "OTDR", new[] { "", "OUTD(); if (B != 0) { PC -= 2; Tick(5); }" }, new[] { 4, 3, 5 }));

        // --- DD/FD Instructions ---
        var ddInstructions = TransformToIndexed(baseInstructions.Where(i => i.Opcode != 0xF9).ToList(), "_ix", "_ixh", "_ixl", regs, regSetters);
        var fdInstructions = TransformToIndexed(baseInstructions.Where(i => i.Opcode != 0xF9).ToList(), "_iy", "_iyh", "_iyl", regs, regSetters);

        // Explicitly handle LD SP, IX/IY (10 cycles)
        ddInstructions.Add(new Instruction(0xF9, "LD SP, IX", new[] { "SP = _ix" }, new[] { 6 })); // 4(DD) + 6 = 10
        fdInstructions.Add(new Instruction(0xF9, "LD SP, IY", new[] { "SP = _iy" }, new[] { 6 }));

        // --- DDCB / FDCB Instructions ---
        var ddcbInstructions = new List<Instruction>();
        var fdcbInstructions = new List<Instruction>();
        
        for (int op = 0; op < 256; op++) {
            byte opcode = (byte)op;
            int bit = (opcode >> 3) & 0x07;
            int reg = opcode & 0x07;
            int type = (opcode >> 3) & 0x07;
            
            string mnem;
            string act;
            int[] cycles;
            
            if (opcode < 0x40) { // Shifts
                mnem = $"{shiftNames[type]} (IX+d)";
                act = "{ byte val = _bus.Read(WZ); val = DoShift(" + type + ", val); _bus.Write(WZ, val); if (" + reg + " != 6) " + string.Format(regSetters[reg], "val") + "; }";
                cycles = new[] { 4, 4, 3, 3, 3, 3, 3 }; // 23 total
            } else if (opcode < 0x80) { // BIT
                mnem = $"BIT {bit}, (IX+d)";
                act = "{ byte val = _bus.Read(WZ); DoBit(" + bit + ", val); SetUndocumentedFlagsFromWZ(); }";
                cycles = new[] { 4, 4, 3, 3, 3, 3 }; // 20 total
            } else if (opcode < 0xC0) { // RES
                mnem = $"RES {bit}, (IX+d)";
                act = "{ byte val = _bus.Read(WZ); val = (byte)(val & ~(1 << " + bit + ")); _bus.Write(WZ, val); if (" + reg + " != 6) " + string.Format(regSetters[reg], "val") + "; }";
                cycles = new[] { 4, 4, 3, 3, 3, 3, 3 }; // 23 total
            } else { // SET
                mnem = $"SET {bit}, (IX+d)";
                act = "{ byte val = _bus.Read(WZ); val = (byte)(val | (1 << " + bit + ")); _bus.Write(WZ, val); if (" + reg + " != 6) " + string.Format(regSetters[reg], "val") + "; }";
                cycles = new[] { 4, 4, 3, 3, 3, 3, 3 }; // 23 total
            }

            ddcbInstructions.Add(new Instruction(opcode, mnem, act, cycles));
            fdcbInstructions.Add(new Instruction(opcode, mnem.Replace("IX", "IY"), act.Replace("_ix", "_iy"), cycles));
        }

        // Generate the file
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file is generated by CpuZ80.CodeGen.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("namespace CpuZ80.Core;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class Cpu");
        sb.AppendLine("{");
        
        // Base Table
        sb.AppendLine("    private void StepGenerated(byte opcode)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        var baseOpcodes = new HashSet<byte>();
        foreach (var inst in baseInstructions.OrderBy(i => i.Opcode)) {
            GenerateCase(sb, inst);
            baseOpcodes.Add(inst.Opcode);
        }
        sb.AppendLine("            case 0xCB: HandleCBGenerated(); break;");
        sb.AppendLine("            case 0xED: HandleEDGenerated(); break;");
        sb.AppendLine("            case 0xDD: HandleDD(); break;");
        sb.AppendLine("            case 0xFD: HandleFD(); break;");
        sb.AppendLine("            default: throw new System.NotImplementedException($\"Opcode 0x{opcode:X2} not implemented in generator.\");");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CB Table
        sb.AppendLine("    private void HandleCBGenerated()");
        sb.AppendLine("    {");
        sb.AppendLine("        Tick(4); // CB Prefix");
        sb.AppendLine("        byte opcode = Fetch();");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in cbInstructions.OrderBy(i => i.Opcode)) {
            GenerateCase(sb, inst);
        }
        sb.AppendLine("            default: throw new System.NotImplementedException($\"CB Opcode 0x{opcode:X2} not implemented in generator.\");");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ED Table
        sb.AppendLine("    private void HandleEDGenerated()");
        sb.AppendLine("    {");
        sb.AppendLine("        Tick(4); // ED Prefix");
        sb.AppendLine("        byte opcode = Fetch();");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in edInstructions.OrderBy(i => i.Opcode).GroupBy(i => i.Opcode).Select(g => g.First())) {
            GenerateCase(sb, inst);
        }
        sb.AppendLine("            default: Tick(4); break; // Invalid ED fetch (4) + ED (4) = 8");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // DD Table
        sb.AppendLine("    private void HandleDD()");
        sb.AppendLine("    {");
        sb.AppendLine("        Tick(4); // DD Prefix");
        sb.AppendLine("        byte opcode = Fetch();");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in ddInstructions.OrderBy(i => i.Opcode)) {
            GenerateCase(sb, inst);
        }
        sb.AppendLine("            case 0xCB: HandleDDCBGenerated(); break;");
        sb.AppendLine("            default: StepBaseOnly(opcode); break;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FD Table
        sb.AppendLine("    private void HandleFD()");
        sb.AppendLine("    {");
        sb.AppendLine("        Tick(4); // FD Prefix");
        sb.AppendLine("        byte opcode = Fetch();");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in fdInstructions.OrderBy(i => i.Opcode)) {
            GenerateCase(sb, inst);
        }
        sb.AppendLine("            case 0xCB: HandleFDCBGenerated(); break;");
        sb.AppendLine("            default: StepBaseOnly(opcode); break;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // DDCB Table
        sb.AppendLine("    private void HandleDDCBGenerated()");
        sb.AppendLine("    {");
        sb.AppendLine("        Tick(4); // CB fetch");
        sb.AppendLine("        sbyte d = (sbyte)Fetch(); // M3");
        sb.AppendLine("        WZ = (ushort)(_ix + d);");
        sb.AppendLine("        byte opcode = Fetch(); // M4");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in ddcbInstructions.OrderBy(i => i.Opcode)) {
             var skippedCycles = inst.Cycles.Skip(2).ToArray();
             var skippedActions = inst.Actions.Skip(2).ToArray();
             var skippedInst = inst with { Cycles = skippedCycles, Actions = skippedActions };
             GenerateCase(sb, skippedInst);
        }
        sb.AppendLine("            default: throw new System.NotImplementedException($\"DDCB Opcode 0x{opcode:X2} not implemented.\");");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FDCB Table
        sb.AppendLine("    private void HandleFDCBGenerated()");
        sb.AppendLine("    {");
        sb.AppendLine("        Tick(4); // CB fetch");
        sb.AppendLine("        sbyte d = (sbyte)Fetch();");
        sb.AppendLine("        WZ = (ushort)(_iy + d);");
        sb.AppendLine("        byte opcode = Fetch();");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in fdcbInstructions.OrderBy(i => i.Opcode)) {
             var skippedCycles = inst.Cycles.Skip(2).ToArray();
             var skippedActions = inst.Actions.Skip(2).ToArray();
             var skippedInst = inst with { Cycles = skippedCycles, Actions = skippedActions };
             GenerateCase(sb, skippedInst);
        }
        sb.AppendLine("            default: throw new System.NotImplementedException($\"FDCB Opcode 0x{opcode:X2} not implemented.\");");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        GenerateStepBaseOnly(sb, baseInstructions);

        // Helper
        sb.AppendLine("    private bool IsGenerated(byte opcode)");
        sb.AppendLine("    {");
        
        var generatedSet = new HashSet<byte>(baseOpcodes);
        generatedSet.Add(0xCB);
        generatedSet.Add(0xED);
        generatedSet.Add(0xDD);
        generatedSet.Add(0xFD);

        if (generatedSet.Count == 256) {
            sb.AppendLine("        return true;");
        } else {
            sb.AppendLine("        return opcode switch {");
            foreach (var op in generatedSet.OrderBy(o => o)) sb.AppendLine($"            0x{op:X2} => true,");
            sb.AppendLine("            _ => false");
            sb.AppendLine("        };");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        System.IO.File.WriteAllText(outputPath, sb.ToString());
        System.Console.WriteLine($"Generated {outputPath}");
    }

    static void GenerateStepBaseOnly(StringBuilder sb, List<Instruction> baseInstructions) {
         sb.AppendLine("    private void StepBaseOnly(byte opcode)");
         sb.AppendLine("    {");
         sb.AppendLine("        switch (opcode)");
         sb.AppendLine("        {");
         foreach (var inst in baseInstructions.OrderBy(i => i.Opcode)) {
             GenerateCase(sb, inst);
         }
         sb.AppendLine("            case 0xCB: HandleCBGenerated(); break;");
         sb.AppendLine("            case 0xED: HandleEDGenerated(); break;");
         sb.AppendLine("            case 0xDD: HandleDD(); break;");
         sb.AppendLine("            case 0xFD: HandleFD(); break;");
         sb.AppendLine("            default: throw new System.NotImplementedException($\"Opcode 0x{opcode:X2} not implemented.\");");
         sb.AppendLine("        }");
         sb.AppendLine("    }");
    }

    static void GenerateCase(StringBuilder sb, Instruction inst) {
        sb.Append($"            case 0x{inst.Opcode:X2}: /* {inst.Mnemonic} */ {{ ");
        
        if (inst.Cycles.Length == 0) {
            foreach (var action in inst.Actions) {
                if (!string.IsNullOrEmpty(action)) sb.Append($"{action}; ");
            }
            sb.AppendLine("} break;");
            return;
        }

        for (int i = 0; i < inst.Cycles.Length; i++) {
            sb.Append($"Tick({inst.Cycles[i]}); ");
            
            // For single-action instructions (legacy), execute action after the first Tick.
            // For multi-action instructions, execute actions interleaved.
            if (inst.Actions.Length == 1) {
                if (i == 0 && !string.IsNullOrEmpty(inst.Actions[0])) {
                    sb.Append($"{inst.Actions[0]}; ");
                }
            } else if (i < inst.Actions.Length && !string.IsNullOrEmpty(inst.Actions[i])) {
                sb.Append($"{inst.Actions[i]}; ");
            }
        }
        sb.AppendLine("} break;");
    }

    static List<Instruction> TransformToIndexed(List<Instruction> baseInsts, string reg16, string regH, string regL, string[] regs, string[] regSetters) {
        var result = new List<Instruction>();
        foreach (var inst in baseInsts) {
            bool usesHlPtr = inst.Mnemonic.Contains("(HL)");
            bool usesHl = inst.Mnemonic.Contains(" HL") || inst.Mnemonic.Contains("HL,") || inst.Mnemonic == "HL";
            bool usesH = inst.Mnemonic.Contains(" H") || inst.Mnemonic.Contains("H,") || inst.Mnemonic == "H" || inst.Mnemonic.EndsWith(" H");
            bool usesL = inst.Mnemonic.Contains(" L") || inst.Mnemonic.Contains("L,") || inst.Mnemonic == "L" || inst.Mnemonic.EndsWith(" L");

            if (!usesHlPtr && !usesHl && !usesH && !usesL) continue;

            string mnemonic = inst.Mnemonic;
            string[] actions = (string[])inst.Actions.Clone();
            int[] cycles = (int[])inst.Cycles.Clone();

            if (usesHlPtr && mnemonic != "JP (HL)" && mnemonic != "EX (SP), HL") {
                mnemonic = mnemonic.Replace("(HL)", $"({reg16}+d)");
                
                var newActions = new List<string> { "" }; // M2 Opcode fetch logic (prefix was M1)
                var newCycles = new List<int> { 4 }; // M2: opcode fetch
                
                // M3: Fetch displacement
                newActions.Add("sbyte d = (sbyte)Fetch()");
                newCycles.Add(3);

                // M4: Calculate effective address into WZ (internal delay)
                newActions.Add($"WZ = (ushort)({reg16} + d)");
                newCycles.Add(5);

                // Add original actions (M5+)
                for (int i = 0; i < actions.Length; i++) {
                    string act = actions[i];
                    if (string.IsNullOrEmpty(act)) continue;

                    act = act.Replace("_bus.Read(HL)", "_bus.Read(WZ)");
                    act = act.Replace("_bus.Write(HL,", "_bus.Write(WZ,");
                    act = act.Replace("HL++", "WZ++"); 
                    act = act.Replace("HL--", "WZ--");
                    act = act.Replace("SetReg(6,", "SetRegWZ(");

                    newActions.Add(act);
                    if (i > 0) newCycles.Add(cycles[i]);
                    else newCycles.Add(3); // M5 read/write
                }

                actions = newActions.ToArray();
                cycles = newCycles.ToArray();
            } else {
                // Register redirection
                mnemonic = mnemonic.Replace("HL", reg16);
                mnemonic = mnemonic.Replace(" H", " " + regH).Replace("H,", regH + ",").Replace("(H)", "(" + regH + ")");
                mnemonic = mnemonic.Replace(" L", " " + regL).Replace("L,", regL + ",").Replace("(L)", "(" + regL + ")");
                
                for (int i = 0; i < actions.Length; i++) {
                    if (string.IsNullOrEmpty(actions[i])) continue;

                    actions[i] = actions[i].Replace("HL", reg16);
                    actions[i] = actions[i].Replace(" H ", $" {regH} ");
                    actions[i] = actions[i].Replace(" L ", $" {regL} ");
                    actions[i] = actions[i].Replace(" H;", $" {regH};");
                    actions[i] = actions[i].Replace(" L;", $" {regL};");
                    actions[i] = actions[i].Replace("(H)", $"({regH})");
                    actions[i] = actions[i].Replace("(L)", $"({regL})");
                    actions[i] = actions[i].Replace("H = ", $"{regH} = ");
                    actions[i] = actions[i].Replace("L = ", $"{regL} = ");
                    actions[i] = actions[i].Replace("H = {0}", $"{regH} = {{0}}");
                    actions[i] = actions[i].Replace("L = {0}", $"{regL} = {{0}}");
                    actions[i] = actions[i].Replace("DoInc(H)", $"DoInc({regH})");
                    actions[i] = actions[i].Replace("DoInc(L)", $"DoInc({regL})");
                    actions[i] = actions[i].Replace("DoDec(H)", $"DoDec({regH})");
                    actions[i] = actions[i].Replace("DoDec(L)", $"DoDec({regL})");
                    
                    for (int r = 0; r < 8; r++) {
                         actions[i] = actions[i].Replace(string.Format(regSetters[r], "H"), string.Format(regSetters[r], regH));
                         actions[i] = actions[i].Replace(string.Format(regSetters[r], "L"), string.Format(regSetters[r], regL));
                    }
                    
                    // Final properties safety
                    actions[i] = actions[i].Replace(" H ", $" {regH} ").Replace(" L ", $" {regL} ");
                    actions[i] = actions[i].Replace(" H,", $" {regH},").Replace(" L,", $" {regL},");
                    actions[i] = actions[i].Replace(",H", $",{regH}");
                    actions[i] = actions[i].Replace(",L", $",{regL}");
                }
            }
            
            result.Add(new Instruction(inst.Opcode, mnemonic, actions, cycles));
        }
        return result;
    }
}
