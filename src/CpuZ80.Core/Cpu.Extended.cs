namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private readonly Action[] _edOps = new Action[256];
    private byte I, R;
    private int _interruptMode = 0;

    private void BuildEdDispatchTable()
    {
        for (int i = 0; i < 256; i++)
        {
            _edOps[i] = () => throw new NotImplementedException($"ED Opcode 0x{_bus.Read((ushort)(PC - 1)):X2} not implemented");
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

        // LD I, A / LD R, A and vice-versa
        _edOps[0x47] = () => { I = A; TotalCycles += 9UL; };
        _edOps[0x4F] = () => { R = A; TotalCycles += 9UL; };
        _edOps[0x57] = () => { A = I; SetLogicFlags(A); TotalCycles += 9UL; };
        _edOps[0x5F] = () => { A = R; SetLogicFlags(A); TotalCycles += 9UL; };

        // IM x
        _edOps[0x46] = () => { _interruptMode = 0; TotalCycles += 8UL; };
        _edOps[0x56] = () => { _interruptMode = 1; TotalCycles += 8UL; };
        _edOps[0x5E] = () => { _interruptMode = 2; TotalCycles += 8UL; };

        // Block Operations
        _edOps[0xA0] = LDI;
        _edOps[0xA1] = CPI;
        _edOps[0xA8] = LDD;
        _edOps[0xA9] = CPD;
        _edOps[0xB0] = LDIR;
        _edOps[0xB1] = CPIR;
        _edOps[0xB8] = LDDR;
        _edOps[0xB9] = CPDR;
    }

    private void DoAdc16(ushort val)
    {
        int carry = FlagC ? 1 : 0;
        int res = HL + val + carry;
        FlagN = false;
        FlagH = ((HL & 0x0FFF) + (val & 0x0FFF) + carry > 0x0FFF);
        FlagPV = (((HL ^ res) & (val ^ res)) & 0x8000) != 0;
        FlagC = res > 0xFFFF;
        HL = (ushort)(res & 0xFFFF);
        FlagZ = HL == 0;
        FlagS = (HL & 0x8000) != 0;
        TotalCycles += 15UL;
    }

    private void DoSbc16(ushort val)
    {
        int carry = FlagC ? 1 : 0;
        int res = HL - val - carry;
        FlagN = true;
        FlagH = ((HL & 0x0FFF) - (val & 0x0FFF) - carry < 0);
        FlagPV = (((HL ^ val) & (HL ^ res)) & 0x8000) != 0;
        FlagC = res < 0;
        HL = (ushort)(res & 0xFFFF);
        FlagZ = HL == 0;
        FlagS = (HL & 0x8000) != 0;
        TotalCycles += 15UL;
    }

    private void LDI()
    {
        byte val = _bus.Read(HL++);
        _bus.Write(DE++, val);
        BC--;
        FlagN = false;
        FlagH = false;
        FlagPV = BC != 0;
        TotalCycles += 16UL;
    }

    private void LDIR()
    {
        LDI();
        if (BC != 0)
        {
            PC -= 2; // Repeat LDIR (ED B0)
            TotalCycles += 5UL; // 21 cycles total when repeating
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
        TotalCycles += 16UL;
    }

    private void LDDR()
    {
        LDD();
        if (BC != 0)
        {
            PC -= 2;
            TotalCycles += 5UL;
        }
    }

    private void CPI()
    {
        byte val = _bus.Read(HL++);
        byte res = (byte)(A - val);
        BC--;
        FlagN = true;
        FlagH = (A & 0x0F) < (val & 0x0F);
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = BC != 0;
        TotalCycles += 16UL;
    }

    private void CPIR()
    {
        CPI();
        if (BC != 0 && !FlagZ)
        {
            PC -= 2;
            TotalCycles += 5UL;
        }
    }

    private void CPD()
    {
        byte val = _bus.Read(HL--);
        byte res = (byte)(A - val);
        BC--;
        FlagN = true;
        FlagH = (A & 0x0F) < (val & 0x0F);
        FlagZ = res == 0;
        FlagS = (res & 0x80) != 0;
        FlagPV = BC != 0;
        TotalCycles += 16UL;
    }

    private void CPDR()
    {
        CPD();
        if (BC != 0 && !FlagZ)
        {
            PC -= 2;
            TotalCycles += 5UL;
        }
    }

    private void HandleED()
    {
        byte opcode = Fetch();
        _edOps[opcode]();
    }
}
