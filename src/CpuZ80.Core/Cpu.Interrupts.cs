namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private void AcceptNmi()
    {
        IFF2 = IFF1;
        IFF1 = false;
        Push(PC);
        PC = 0x0066;
        Tick(11);
    }

    private void AcceptInt()
    {
        IFF1 = false;
        IFF2 = false;
        Tick(2); // interrupt acknowledge cycles

        // A host holding INT asserted until acknowledged clears it here.
        _host.OnInterruptAcknowledged(this);

        switch (_interruptMode)
        {
            case 0:
                // Device places opcode on bus
                _isInterruptFetch = true;
                StepGenerated(_intDataBus);
                _isInterruptFetch = false;
                break;
            case 1:
                Push(PC);
                PC = 0x0038;
                Tick(11);
                break;
            case 2:
                ushort vectorAddr = (ushort)((I << 8) | _intDataBus);
                ushort dest = ReadWord(vectorAddr);
                Push(PC);
                PC = dest;
                Tick(17);
                break;
        }
    }
}

