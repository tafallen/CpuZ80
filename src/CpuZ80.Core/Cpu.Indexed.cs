namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void HandleDD()
    {
        _indexMode = IndexMode.IX;
        _hasIdxAddr = false;
        byte opcode = Fetch();
        if (opcode == 0xCB)
            HandleIndexedBitwise();
        else if (opcode == 0x36) // LD (IX+d), n — encoding is d then n, not n then (IX+d)
        {
            sbyte d = (sbyte)Fetch();
            byte n = Fetch();
            _bus.Write((ushort)(_ix + d), n);
            TotalCycles += 15UL; // 19 total (prefix adds 4 below)
        }
        else
            _ops[opcode]();
        _indexMode = IndexMode.HL;
        _hasIdxAddr = false;
        TotalCycles += 4UL;
    }

    private void HandleFD()
    {
        _indexMode = IndexMode.IY;
        _hasIdxAddr = false;
        byte opcode = Fetch();
        if (opcode == 0xCB)
            HandleIndexedBitwise();
        else if (opcode == 0x36) // LD (IY+d), n
        {
            sbyte d = (sbyte)Fetch();
            byte n = Fetch();
            _bus.Write((ushort)(_iy + d), n);
            TotalCycles += 15UL;
        }
        else
            _ops[opcode]();
        _indexMode = IndexMode.HL;
        _hasIdxAddr = false;
        TotalCycles += 4UL;
    }

    private void HandleIndexedBitwise()
    {
        ushort reg = _indexMode == IndexMode.IX ? _ix : _iy;
        sbyte d = (sbyte)Fetch();
        byte opcode = Fetch();
        ushort addr = (ushort)(reg + d);
        byte val = _bus.Read(addr);
        int bit = (opcode >> 3) & 0x07;
        int r = opcode & 0x07;

        if (opcode < 0x40) // Rotates and Shifts
        {
            val = DoShift((opcode >> 3) & 0x07, val);
            _bus.Write(addr, val);
            if (r != 6) SetReg(r, val);
            TotalCycles = TotalCycles - 4UL + 23UL;
        }
        else if (opcode < 0x80) // BIT
        {
            DoBit(bit, val);
            TotalCycles = TotalCycles - 4UL + 20UL;
        }
        else if (opcode < 0xC0) // RES
        {
            val = (byte)(val & ~(1 << bit));
            _bus.Write(addr, val);
            if (r != 6) SetReg(r, val);
            TotalCycles = TotalCycles - 4UL + 23UL;
        }
        else // SET
        {
            val = (byte)(val | (1 << bit));
            _bus.Write(addr, val);
            if (r != 6) SetReg(r, val);
            TotalCycles = TotalCycles - 4UL + 23UL;
        }
    }

    public ushort IX { get => _ix; set => _ix = value; }
    public ushort IY { get => _iy; set => _iy = value; }

    private void DoAdd16Indexed(ref ushort reg, ushort val)
    {
        int res = reg + val;
        FlagN = false;
        FlagH = (((reg & 0x0FFF) + (val & 0x0FFF)) & 0x1000) != 0;
        FlagC = (res & 0x10000) != 0;
        reg = (ushort)(res & 0xFFFF);
        F = (byte)((F & ~0x28) | ((reg >> 8) & 0x28));
        TotalCycles += 11UL; 
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
}
