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

            case 0xE1: indexReg = Pop(); TotalCycles += 14UL; break;
            case 0xE5: Push(indexReg); TotalCycles += 15UL; break;
            case 0xF9: SP = indexReg; TotalCycles += 10UL; break;

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

            case 0xCB: HandleIndexedBitwise(ref indexReg); break;

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
                        // 8-bit halves of IX/IY support
                        byte val = GetRegIndexed(src, ref indexReg);
                        SetRegIndexed(dest, val, ref indexReg);
                        TotalCycles += 8UL; 
                    }
                }
                else if ((opcode & 0xF0) == 0x80 || (opcode & 0xF0) == 0x90 || (opcode & 0xF0) == 0xA0 || (opcode & 0xF0) == 0xB0)
                {
                    // Arithmetic block
                    if ((opcode & 0x07) == 6)
                    {
                        ushort addr = (ushort)(indexReg + (sbyte)Fetch());
                        byte val = _bus.Read(addr);
                        ExecuteArithmetic(opcode, val);
                        TotalCycles += 19UL;
                    }
                    else
                    {
                        byte val = GetRegIndexed(opcode & 0x07, ref indexReg);
                        ExecuteArithmetic(opcode, val);
                        TotalCycles += 8UL;
                    }
                }
                else
                {
                    _ops[opcode]();
                    TotalCycles += 4UL;
                }
                break;
        }
    }

    private byte GetRegIndexed(int index, ref ushort indexReg)
    {
        if (index == 4) return (byte)(indexReg >> 8);
        if (index == 5) return (byte)(indexReg & 0xFF);
        return GetReg(index);
    }

    private void SetRegIndexed(int index, byte val, ref ushort indexReg)
    {
        if (index == 4) indexReg = (ushort)((val << 8) | (indexReg & 0xFF));
        else if (index == 5) indexReg = (ushort)((indexReg & 0xFF00) | val);
        else SetReg(index, val);
    }

    private void DoAdd16Indexed(ref ushort reg, ushort val)
    {
        int res = reg + val;
        FlagN = false;
        FlagH = (((reg & 0x0FFF) + (val & 0x0FFF)) & 0x1000) != 0;
        FlagC = (res & 0x10000) != 0;
        reg = (ushort)(res & 0xFFFF);
        F = (byte)((F & ~0x28) | ((reg >> 8) & 0x28));
        TotalCycles += 15UL;
    }

    private void ExecuteArithmetic(byte opcode, byte val)
    {
        int type = (opcode >> 3) & 0x07;
        switch (type)
        {
            case 0: AddInternal(val, false); TotalCycles -= 4UL; break;
            case 1: AddInternal(val, true);  TotalCycles -= 4UL; break;
            case 2: SubInternal(val, false); TotalCycles -= 4UL; break;
            case 3: SubInternal(val, true);  TotalCycles -= 4UL; break;
            case 4: A &= val; SetLogicFlags(A); break;
            case 5: A ^= val; SetLogicFlags(A); break;
            case 6: A |= val; SetLogicFlags(A); break;
            case 7: byte oldA = A; SubInternal(val, false); TotalCycles -= 4UL; A = oldA; break;
        }
    }

    private void HandleIndexedBitwise(ref ushort indexReg)
    {
        sbyte d = (sbyte)Fetch();
        byte opcode = Fetch();
        ushort addr = (ushort)(indexReg + d);
        byte val = _bus.Read(addr);
        int bit = (opcode >> 3) & 0x07;
        int reg = opcode & 0x07;

        if (opcode < 0x40) // Rotates and Shifts
        {
            val = DoShift((opcode >> 3) & 0x07, val);
            _bus.Write(addr, val);
            if (reg != 6) SetReg(reg, val);
            TotalCycles += 23UL;
        }
        else if (opcode < 0x80) // BIT
        {
            DoBit(bit, val);
            TotalCycles += 20UL;
        }
        else if (opcode < 0xC0) // RES
        {
            val = (byte)(val & ~(1 << bit));
            _bus.Write(addr, val);
            if (reg != 6) SetReg(reg, val);
            TotalCycles += 23UL;
        }
        else // SET
        {
            val = (byte)(val | (1 << bit));
            _bus.Write(addr, val);
            if (reg != 6) SetReg(reg, val);
            TotalCycles += 23UL;
        }
    }
    }

