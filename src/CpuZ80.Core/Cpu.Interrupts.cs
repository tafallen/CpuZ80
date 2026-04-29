namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void AcceptNmi()
    {
        IFF2 = IFF1;
        IFF1 = false;
        Push(PC);
        PC = 0x0066;
        TotalCycles += 11UL;
    }

    private void AcceptInt()
    {
        IFF1 = false;
        IFF2 = false;
        TotalCycles += 2UL; // interrupt acknowledge cycles

        switch (_interruptMode)
        {
            case 0:
                // Device places opcode on bus; treat as RST to the vector byte
                _ops[_intDataBus]();
                break;
            case 1:
                Push(PC);
                PC = 0x0038;
                TotalCycles += 11UL;
                break;
            case 2:
                ushort vectorAddr = (ushort)((I << 8) | _intDataBus);
                ushort dest = ReadWord(vectorAddr);
                Push(PC);
                PC = dest;
                TotalCycles += 17UL;
                break;
        }
    }
}
