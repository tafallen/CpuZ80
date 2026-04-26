namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void HandleDD() => HandleIndexed(ref _ix);
    private void HandleFD() => HandleIndexed(ref _iy);

    private ushort _ix, _iy;
    public ushort IX { get => _ix; internal set => _ix = value; }
    public ushort IY { get => _iy; internal set => _iy = value; }

    private void HandleIndexed(ref ushort indexReg)
    {
        byte opcode = Fetch();
        
        // The Z80 index prefixes (DD/FD) basically swap HL for IX/IY in the base table.
        // Some instructions use a displacement byte, some don't.
        
        switch (opcode)
        {
            case 0x09: DoAdd16Indexed(ref indexReg, BC); break;
            case 0x19: DoAdd16Indexed(ref indexReg, DE); break;
            case 0x29: DoAdd16Indexed(ref indexReg, indexReg); break;
            case 0x39: DoAdd16Indexed(ref indexReg, SP); break;
            
            case 0x21: indexReg = FetchWord(); TotalCycles += 14UL; break;
            case 0x22: WriteWord(FetchWord(), indexReg); TotalCycles += 20UL; break;
            case 0x2A: indexReg = ReadWord(FetchWord()); TotalCycles += 20UL; break;
            case 0x23: indexReg++; TotalCycles += 10UL; break;
            case 0x2B: indexReg--; TotalCycles += 10UL; break;

            case 0x34: // INC (IX+d)
                {
                    ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                    _bus.Write(addr, DoInc(_bus.Read(addr)));
                    TotalCycles += 23UL;
                }
                break;
            case 0x35: // DEC (IX+d)
                {
                    ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                    _bus.Write(addr, DoDec(_bus.Read(addr)));
                    TotalCycles += 23UL;
                }
                break;
            case 0x36: // LD (IX+d), n
                {
                    ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                    _bus.Write(addr, Fetch());
                    TotalCycles += 19UL;
                }
                break;

            default:
                if ((opcode & 0xC0) == 0x40) // LD r, r' block
                {
                    int dest = (opcode >> 3) & 0x07;
                    int src = opcode & 0x07;
                    if (dest == 6) // LD (IX+d), r
                    {
                        ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                        _bus.Write(addr, GetReg(src));
                        TotalCycles += 19UL;
                    }
                    else if (src == 6) // LD r, (IX+d)
                    {
                        ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                        SetReg(dest, _bus.Read(addr));
                        TotalCycles += 19UL;
                    }
                    else
                    {
                        // Fallback to normal LD r, r' but with extra cycles for prefix
                        _ops[opcode]();
                        TotalCycles += 4UL; 
                    }
                }
                else if ((opcode & 0xF0) == 0x80 || (opcode & 0xF0) == 0x90 || (opcode & 0xF0) == 0xA0 || (opcode & 0xF0) == 0xB0)
                {
                    // Arithmetic block (ADD, ADC, SUB, SBC, AND, XOR, OR, CP)
                    if ((opcode & 0x07) == 6)
                    {
                        ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                        byte val = _bus.Read(addr);
                        ExecuteArithmetic(opcode, val);
                        TotalCycles += 19UL;
                    }
                    else
                    {
                        _ops[opcode]();
                        TotalCycles += 4UL;
                    }
                }
                else
                {
                    throw new NotImplementedException($"Indexed Opcode 0x{opcode:X2} not implemented");
                }
                break;
        }
    }

    private void DoAdd16Indexed(ref ushort reg, ushort val)
    {
        int res = reg + val;
        FlagN = false;
        FlagH = ((reg & 0x0FFF) + (val & 0x0FFF) > 0x0FFF);
        FlagC = res > 0xFFFF;
        reg = (ushort)(res & 0xFFFF);
        TotalCycles += 15UL;
    }

    private void ExecuteArithmetic(byte opcode, byte val)
    {
        // Internal helper to map opcode to arithmetic action without adding cycles (since HandleIndexed handles timing)
        int type = (opcode >> 3) & 0x07;
        switch (type)
        {
            case 0: AddInternal(val, false); break; // ADD
            case 1: AddInternal(val, true);  break; // ADC
            case 2: SubInternal(val, false); break; // SUB
            case 3: SubInternal(val, true);  break; // SBC
            case 4: A &= val; SetLogicFlags(A); break; // AND
            case 5: A ^= val; SetLogicFlags(A); break; // XOR
            case 6: A |= val; SetLogicFlags(A); break; // OR
            case 7: byte oldA = A; SubInternal(val, false); A = oldA; break; // CP
        }
        TotalCycles -= 4UL; // Deduct the cycles added by the helpers so HandleIndexed can set specific timing
    }
}
