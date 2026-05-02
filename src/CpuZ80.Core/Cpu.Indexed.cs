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
            Tick(15); // 19 total (prefix adds 4 below)
        }
        else if (opcode == 0xE9) // JP (IX)
        {
            PC = _ix;
            Tick(4);
        }
        else
            _ops[opcode]();
        _indexMode = IndexMode.HL;
        _hasIdxAddr = false;
        Tick(4);
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
            Tick(15);
        }
        else if (opcode == 0xE9) // JP (IY)
        {
            PC = _iy;
            Tick(4);
        }
        else
            _ops[opcode]();
        _indexMode = IndexMode.HL;
        _hasIdxAddr = false;
        Tick(4);
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
            Tick(-4); Tick(23);
        }
        else if (opcode < 0x80) // BIT
        {
            DoBit(bit, val);
            Tick(-4); Tick(20);
        }
        else if (opcode < 0xC0) // RES
        {
            val = (byte)(val & ~(1 << bit));
            _bus.Write(addr, val);
            if (r != 6) SetReg(r, val);
            Tick(-4); Tick(23);
        }
        else // SET
        {
            val = (byte)(val | (1 << bit));
            _bus.Write(addr, val);
            if (r != 6) SetReg(r, val);
            Tick(-4); Tick(23);
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
        Tick(11);
    }

    private void ExecuteArithmetic(byte opcode, byte val)
    {
        int type = (opcode >> 3) & 0x07;
        switch (type)
        {
            case 0: AddInternal(val, false); Tick(-4); break;
            case 1: AddInternal(val, true);  Tick(-4); break;
            case 2: SubInternal(val, false); Tick(-4); break;
            case 3: SubInternal(val, true);  Tick(-4); break;
            case 4: A &= val; SetLogicFlags(A); break;
            case 5: A ^= val; SetLogicFlags(A); break;
            case 6: A |= val; SetLogicFlags(A); break;
            case 7: byte oldA = A; SubInternal(val, false); Tick(-4); A = oldA; break;
        }
    }
}

