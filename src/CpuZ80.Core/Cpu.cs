using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CpuZ80.Tests")]

namespace CpuZ80.Core;

public sealed partial class Cpu
{
    private readonly IBus _bus;
    private readonly IPortBus? _ports;
    public bool IFF1 { get; set; }
    public bool IFF2 { get; set; }
    private bool _halted;
    private bool _nmiPending;
    private bool _intPending;
    private byte _intDataBus;
    private bool _eiDelay;

    public void TriggerNmi() => _nmiPending = true;
    public void TriggerInt(byte dataBus = 0xFF) { _intPending = true; _intDataBus = dataBus; }

    private ushort _ix, _iy;
    private byte _ixh { get => (byte)(_ix >> 8); set => _ix = (ushort)((value << 8) | (_ix & 0xFF)); }
    private byte _ixl { get => (byte)(_ix & 0xFF); set => _ix = (ushort)((_ix & 0xFF00) | value); }
    private byte _iyh { get => (byte)(_iy >> 8); set => _iy = (ushort)((value << 8) | (_iy & 0xFF)); }
    private byte _iyl { get => (byte)(_iy & 0xFF); set => _iy = (ushort)((_iy & 0xFF00) | value); }

    // ── Registers ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
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
    public byte A_ { get; internal set; }
    public byte F_ { get; internal set; }
    public byte B_ { get; internal set; }
    public byte C_ { get; internal set; }
    public byte D_ { get; internal set; }
    public byte E_ { get; internal set; }
    public byte H_ { get; internal set; }
    public byte L_ { get; internal set; }

    public ushort BC { get => (ushort)((B << 8) | C); set { B = (byte)(value >> 8); C = (byte)value; } }
    public ushort DE { get => (ushort)((D << 8) | E); set { D = (byte)(value >> 8); E = (byte)value; } }
    public ushort HL { get => (ushort)((H << 8) | L); set { H = (byte)(value >> 8); L = (byte)value; } }

    public byte I { get; internal set; }
    public byte R { get; internal set; }

    public ushort IX { get => _ix; set => _ix = value; }
    public ushort IY { get => _iy; set => _iy = value; }

    public ushort PC { get; set; }
    public ushort SP { get; set; }
    public ushort WZ; // Internal temporary register (MEMPTR)

    public ulong TotalCycles { get; private set; }
    public bool WaitPin { get; set; }
    public int WaitCycles { get; set; }

    private void Tick(int count)
    {
        for (int i = 0; i < count; i++)
        {
            TotalCycles++;
            
            // Wait states can be injected via the binary WaitPin (synchronous)
            // or by setting WaitCycles (efficient batch wait).
            while (WaitPin || WaitCycles > 0)
            {
                if (WaitCycles > 0) WaitCycles--;
                TotalCycles++;
            }
        }
    }

    /// <summary>
    /// Machine-specific hook for I/O port timing.
    /// Default Z80 behavior is 4 T-states for the I/O itself (M3 cycle).
    /// </summary>
    private void PortTick(ushort port)
    {
        // Machines like the Spectrum can add contention here.
        Tick(4);
    }

    public Cpu(IBus bus, IPortBus? ports = null)
    {
        _bus = bus;
        _ports = ports;
        // Prefix dispatchers are now generated
    }

    public void Reset()
    {
        PC   = 0x0000;
        SP   = 0xFFFF;
        _ix  = 0;
        _iy  = 0;
        IFF1 = false;
        IFF2 = false;
        _halted     = false;
        _nmiPending = false;
        _intPending = false;
        _eiDelay    = false;
        WZ          = 0;
    }

    public void Step()
    {
        if (_nmiPending)
        {
            _halted = false;
            _nmiPending = false;
            _eiDelay = false;
            AcceptNmi();
            return;
        }
        if (_intPending && IFF1 && !_eiDelay)
        {
            _halted = false;
            _intPending = false;
            AcceptInt();
            return;
        }
        _eiDelay = false;
        if (_halted) { R = (byte)((R & 0x80) | ((R + 1) & 0x7F)); Tick(4); return; }

        byte opcode = Fetch();
        StepGenerated(opcode);
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

    private void SetRegWZ(byte val)
    {
        _bus.Write(WZ, val);
    }

    private byte GetReg(int index)
    {
        return index switch
        {
            0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L, 6 => _bus.Read(HL), 7 => A,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    private ushort GetReg16(int index) // 0=BC, 1=DE, 2=HL, 3=SP
    {
        return index switch
        {
            0 => BC,
            1 => DE,
            2 => HL,
            3 => SP,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    private void SetReg16(int index, ushort val)
    {
        switch (index)
        {
            case 0: BC = val; break;
            case 1: DE = val; break;
            case 2: HL = val; break;
            case 3: SP = val; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

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
