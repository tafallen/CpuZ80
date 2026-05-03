using System.Text;

namespace CpuZ80.CodeGen;

public record Instruction(byte Opcode, string Mnemonic, string Action, int[] Cycles, string? WzAction = null);

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
        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x01 | (i << 4)), $"LD {dd[i]}, nn", $"{dd[i]} = FetchWord()", new[] { 4, 3, 3 }));
        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x03 | (i << 4)), $"INC {dd[i]}", $"{dd[i]}++", new[] { 6 }));
        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x0B | (i << 4)), $"DEC {dd[i]}", $"{dd[i]}--", new[] { 6 }));
        for (int i = 0; i < 8; i++) baseInstructions.Add(new Instruction((byte)(0x06 | (i << 3)), $"LD {regs[i]}, n", string.Format(regSetters[i], "Fetch()"), (i == 6 ? new[] { 4, 3, 3 } : new[] { 4, 3 })));

        baseInstructions.Add(new Instruction(0x02, "LD (BC), A", "_bus.Write(BC, A)", new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x12, "LD (DE), A", "_bus.Write(DE, A)", new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x0A, "LD A, (BC)", "A = _bus.Read(BC)", new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x1A, "LD A, (DE)", "A = _bus.Read(DE)", new[] { 4, 3 }));
        baseInstructions.Add(new Instruction(0x22, "LD (nn), HL", "WriteWord(FetchWord(), HL)", new[] { 4, 3, 3, 3, 3 }));
        baseInstructions.Add(new Instruction(0x2A, "LD HL, (nn)", "HL = ReadWord(FetchWord())", new[] { 4, 3, 3, 3, 3 }));
        baseInstructions.Add(new Instruction(0x32, "LD (nn), A", "_bus.Write(FetchWord(), A)", new[] { 4, 3, 3, 3 }));
        baseInstructions.Add(new Instruction(0x3A, "LD A, (nn)", "A = _bus.Read(FetchWord())", new[] { 4, 3, 3, 3 }));

        for (int i = 0; i < 4; i++) baseInstructions.Add(new Instruction((byte)(0x09 | (i << 4)), $"ADD HL, {dd[i]}", $"DoAdd16({dd[i]})", new int[0])); // DoAdd16 calls Tick
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
            baseInstructions.Add(new Instruction((byte)(0xC0 | (cc << 3)), $"RET {condNames[cc]}", $"if ({conditions[cc]}) {{ PC = Pop(); Tick(6); }}", new[] { 5 }));
            baseInstructions.Add(new Instruction((byte)(0xC2 | (cc << 3)), $"JP {condNames[cc]}, nn", $"{{ ushort nn = FetchWord(); if ({conditions[cc]}) PC = nn; }}", new[] { 4, 3, 3 }));
            baseInstructions.Add(new Instruction((byte)(0xC4 | (cc << 3)), $"CALL {condNames[cc]}, nn", $"{{ ushort nn = FetchWord(); if ({conditions[cc]}) {{ Push(PC); PC = nn; Tick(7); }} }}", new[] { 4, 3, 3 }));
            if (cc < 4) baseInstructions.Add(new Instruction((byte)(0x20 | (cc << 3)), $"JR {condNames[cc]}, e", $"{{ sbyte e = (sbyte)Fetch(); if ({conditions[cc]}) {{ PC = (ushort)(PC + e); Tick(5); }} }}", new[] { 4, 3 }));
        }

        string[] qq = { "BC", "DE", "HL", "AF" };
        for (int i = 0; i < 4; i++) {
            string val = (qq[i] == "AF") ? "((A << 8) | F)" : qq[i];
            baseInstructions.Add(new Instruction((byte)(0xC5 | (i << 4)), $"PUSH {qq[i]}", $"Push((ushort){val})", new[] { 4, 3, 4 }));
            string popAction = (qq[i] == "AF") ? "{ ushort val = Pop(); A = (byte)(val >> 8); F = (byte)(val & 0xFF); }" : $"{qq[i]} = Pop()";
            baseInstructions.Add(new Instruction((byte)(0xC1 | (i << 4)), $"POP {qq[i]}", popAction, new[] { 4, 3, 3 }));
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
        baseInstructions.Add(new Instruction(0x08, "EX AF, AF'", "EX_AF_AF()", new int[0]));
        baseInstructions.Add(new Instruction(0xD9, "EXX", "EXX()", new int[0]));
        baseInstructions.Add(new Instruction(0xEB, "EX DE, HL", "(DE, HL) = (HL, DE)", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xE3, "EX (SP), HL", "{ ushort tmp = HL; HL = ReadWord(SP); WriteWord(SP, tmp); }", new[] { 4, 3, 4, 3, 5 }));
        baseInstructions.Add(new Instruction(0xF3, "DI", "IFF1 = IFF2 = false", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xFB, "EI", "IFF1 = IFF2 = true; _eiDelay = true;", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x27, "DAA", "DAA()", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x2F, "CPL", "A = (byte)~A; FlagN = true; FlagH = true;", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x37, "SCF", "FlagC = true; FlagN = false; FlagH = false;", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x3F, "CCF", "FlagH = FlagC; FlagC = !FlagC; FlagN = false;", new[] { 4 }));
        baseInstructions.Add(new Instruction(0xF9, "LD SP, HL", "SP = HL", new[] { 6 }));
        baseInstructions.Add(new Instruction(0xE9, "JP (HL)", "PC = HL", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x76, "HALT", "{ _halted = true; PC--; }", new[] { 4 }));
        baseInstructions.Add(new Instruction(0x10, "DJNZ e", "{ sbyte e = (sbyte)Fetch(); B--; if (B != 0) { PC = (ushort)(PC + e); Tick(5); } }", new[] { 4, 4 }));
        baseInstructions.Add(new Instruction(0xD3, "OUT (n), A", "{ _ports?.Out((ushort)((A << 8) | Fetch()), A); }", new[] { 4, 3, 4 }));
        baseInstructions.Add(new Instruction(0xDB, "IN A, (n)", "{ A = _ports?.In((ushort)((A << 8) | Fetch())) ?? 0xFF; }", new[] { 4, 3, 4 }));

        // --- CB Instructions ---
        string[] shiftNames = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };
        for (int op = 0; op < 8; op++)
            for (int s = 0; s < 8; s++)
                cbInstructions.Add(new Instruction((byte)(op << 3 | s), $"{shiftNames[op]} {regs[s]}", string.Format(regSetters[s], $"DoShift({op}, {regs[s]})"), (s == 6 ? new[] { 4, 4, 4, 3 } : new[] { 4, 4 })));

        for (int bit = 0; bit < 8; bit++)
            for (int s = 0; s < 8; s++) {
                cbInstructions.Add(new Instruction((byte)(0x40 | (bit << 3) | s), $"BIT {bit}, {regs[s]}", $"DoBit({bit}, {regs[s]})", (s == 6 ? new[] { 4, 4, 4 } : new[] { 4, 4 })));
                cbInstructions.Add(new Instruction((byte)(0x80 | (bit << 3) | s), $"RES {bit}, {regs[s]}", string.Format(regSetters[s], $" (byte)({regs[s]} & ~(1 << {bit}))"), (s == 6 ? new[] { 4, 4, 4, 3 } : new[] { 4, 4 })));
                cbInstructions.Add(new Instruction((byte)(0xC0 | (bit << 3) | s), $"SET {bit}, {regs[s]}", string.Format(regSetters[s], $" (byte)({regs[s]} | (1 << {bit}))"), (s == 6 ? new[] { 4, 4, 4, 3 } : new[] { 4, 4 })));
            }

        // --- ED Instructions ---
        for (int i = 0; i < 4; i++) {
            edInstructions.Add(new Instruction((byte)(0x4A | (i << 4)), $"ADC HL, {dd[i]}", $"DoAdc16({dd[i]})", new int[0]));
            edInstructions.Add(new Instruction((byte)(0x42 | (i << 4)), $"SBC HL, {dd[i]}", $"DoSbc16({dd[i]})", new int[0]));
        }
        edInstructions.Add(new Instruction(0x67, "RRD", "RRD()", new int[0]));
        edInstructions.Add(new Instruction(0x6F, "RLD", "RLD()", new int[0]));
        for (int i = 0; i < 3; i++) {
            if (i == 2) continue; // SP handle separately
            edInstructions.Add(new Instruction((byte)(0x4B | (i << 4)), $"LD {dd[i]}, (nn)", $"{dd[i]} = ReadWord(FetchWord())", new[] { 4, 4, 3, 3, 3, 3 }));
            edInstructions.Add(new Instruction((byte)(0x43 | (i << 4)), $"LD (nn), {dd[i]}", "WriteWord(FetchWord(), " + dd[i] + ")", new[] { 4, 4, 3, 3, 3, 3 }));
        }
        edInstructions.Add(new Instruction(0x7B, "LD SP, (nn)", "SP = ReadWord(FetchWord())", new[] { 4, 4, 3, 3, 3, 3 }));
        edInstructions.Add(new Instruction(0x73, "LD (nn), SP", "WriteWord(FetchWord(), SP)", new[] { 4, 4, 3, 3, 3, 3 }));

        // Undocumented LD (nn), HL duplicates
        edInstructions.Add(new Instruction(0x63, "LD (nn), HL", "WriteWord(FetchWord(), HL)", new[] { 4, 4, 3, 3, 3, 3 }));
        edInstructions.Add(new Instruction(0x6B, "LD HL, (nn)", "HL = ReadWord(FetchWord())", new[] { 4, 4, 3, 3, 3, 3 }));

        edInstructions.Add(new Instruction(0x47, "LD I, A", "I = A", new[] { 4, 5 }));
        edInstructions.Add(new Instruction(0x4F, "LD R, A", "R = A", new[] { 4, 5 }));
        edInstructions.Add(new Instruction(0x57, "LD A, I", "{ A = I; SetLogicFlags(A); FlagPV = IFF2; FlagN = false; FlagH = false; }", new[] { 4, 5 }));
        edInstructions.Add(new Instruction(0x5F, "LD A, R", "{ A = R; SetLogicFlags(A); FlagPV = IFF2; FlagN = false; FlagH = false; }", new[] { 4, 5 }));

        edInstructions.Add(new Instruction(0x46, "IM 0", "_interruptMode = 0", new[] { 4, 4 }));
        edInstructions.Add(new Instruction(0x56, "IM 1", "_interruptMode = 1", new[] { 4, 4 }));
        edInstructions.Add(new Instruction(0x5E, "IM 2", "_interruptMode = 2", new[] { 4, 4 }));
        // IM aliases
        edInstructions.Add(new Instruction(0x4E, "IM 0", "_interruptMode = 0", new[] { 4, 4 }));
        edInstructions.Add(new Instruction(0x66, "IM 0", "_interruptMode = 0", new[] { 4, 4 }));
        edInstructions.Add(new Instruction(0x6E, "IM 0", "_interruptMode = 0", new[] { 4, 4 }));
        edInstructions.Add(new Instruction(0x76, "IM 1", "_interruptMode = 1", new[] { 4, 4 }));
        edInstructions.Add(new Instruction(0x7E, "IM 2", "_interruptMode = 2", new[] { 4, 4 }));

        edInstructions.Add(new Instruction(0x44, "NEG", "NEG()", new int[0]));
        // NEG aliases
        for (byte op = 0x4C; op <= 0x7C; op += 0x08) edInstructions.Add(new Instruction(op, "NEG", "NEG()", new int[0]));
        edInstructions.Add(new Instruction(0x54, "NEG", "NEG()", new int[0]));
        edInstructions.Add(new Instruction(0x64, "NEG", "NEG()", new int[0]));
        edInstructions.Add(new Instruction(0x74, "NEG", "NEG()", new int[0]));

        edInstructions.Add(new Instruction(0x4D, "RETI", "RETI()", new int[0]));
        edInstructions.Add(new Instruction(0x45, "RETN", "RETN()", new int[0]));
        // RETN aliases
        for (byte op = 0x55; op <= 0x7D; op += 0x08) edInstructions.Add(new Instruction(op, "RETN", "RETN()", new int[0]));
        edInstructions.Add(new Instruction(0x5D, "RETN", "RETN()", new int[0]));
        edInstructions.Add(new Instruction(0x6D, "RETN", "RETN()", new int[0]));
        edInstructions.Add(new Instruction(0x7D, "RETN", "RETN()", new int[0]));

        for (int r = 0; r < 8; r++) {
            edInstructions.Add(new Instruction((byte)(0x40 | (r << 3)), $"IN {regs[r]}, (C)", 
                "{ byte val = _ports?.In(BC) ?? 0xFF; if (" + r + " != 6) " + string.Format(regSetters[r], "val") + "; FlagS = (val & 0x80) != 0; FlagZ = val == 0; FlagH = false; FlagPV = GetParity(val); FlagN = false; F = (byte)((F & ~0x28) | (val & 0x28)); }", new[] { 4, 4, 4 }));
            edInstructions.Add(new Instruction((byte)(0x41 | (r << 3)), $"OUT (C), {regs[r]}", 
                "{ byte val = " + (r == 6 ? "(byte)0" : regs[r]) + "; _ports?.Out(BC, val); }", new[] { 4, 4, 4 }));
        }

        edInstructions.Add(new Instruction(0xA0, "LDI", "LDI()", new int[0]));
        edInstructions.Add(new Instruction(0xA1, "CPI", "CPI()", new int[0]));
        edInstructions.Add(new Instruction(0xA8, "LDD", "LDD()", new int[0]));
        edInstructions.Add(new Instruction(0xA9, "CPD", "CPD()", new int[0]));
        edInstructions.Add(new Instruction(0xB0, "LDIR", "LDIR()", new int[0]));
        edInstructions.Add(new Instruction(0xB1, "CPIR", "CPIR()", new int[0]));
        edInstructions.Add(new Instruction(0xB8, "LDDR", "LDDR()", new int[0]));
        edInstructions.Add(new Instruction(0xB9, "CPDR", "CPDR()", new int[0]));
        edInstructions.Add(new Instruction(0xA2, "INI", "INI()", new int[0]));
        edInstructions.Add(new Instruction(0xAA, "IND", "IND()", new int[0]));
        edInstructions.Add(new Instruction(0xA3, "OUTI", "OUTI()", new int[0]));
        edInstructions.Add(new Instruction(0xAB, "OUTD", "OUTD()", new int[0]));
        edInstructions.Add(new Instruction(0xB2, "INIR", "INIR()", new int[0]));
        edInstructions.Add(new Instruction(0xBA, "INDR", "INDR()", new int[0]));
        edInstructions.Add(new Instruction(0xB3, "OTIR", "OTIR()", new int[0]));
        edInstructions.Add(new Instruction(0xBB, "OTDR", "OTDR()", new int[0]));

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
        sb.AppendLine("        byte opcode = Fetch();");
        sb.AppendLine("        switch (opcode)");
        sb.AppendLine("        {");
        foreach (var inst in edInstructions.OrderBy(i => i.Opcode).GroupBy(i => i.Opcode).Select(g => g.First())) {
            GenerateCase(sb, inst);
        }
        sb.AppendLine("            default: Tick(8); break; // Invalid ED opcodes act as NOPs");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

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

    static void GenerateCase(StringBuilder sb, Instruction inst) {
        sb.Append($"            case 0x{inst.Opcode:X2}: /* {inst.Mnemonic} */ ");
        
        if (inst.Cycles.Length == 0) {
            sb.Append($"{inst.Action}; ");
        } else if (inst.Cycles.Length == 1) {
            sb.Append($"Tick({inst.Cycles[0]}); {inst.Action}; ");
        } else {
            // Split action and interleave. For Task 2, we just interleave sequentially before the action
            // but the design spec wants interleaving. 
            // I'll put M1 (always 4 for base) first, then action, then remaining.
            sb.Append($"Tick({inst.Cycles[0]}); ");
            sb.Append($"{inst.Action}; ");
            for (int i = 1; i < inst.Cycles.Length; i++) {
                sb.Append($"Tick({inst.Cycles[i]}); ");
            }
        }

        if (!string.IsNullOrEmpty(inst.WzAction)) {
            sb.Append($"{inst.WzAction}; ");
        }
        sb.AppendLine("break;");
    }
}
