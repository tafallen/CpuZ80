using CpuZ80.Core;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrumPlus3;

/// <summary>
/// Routes I/O between the ULA, both paging latches, the AY and the Kempston
/// interface, using the +2A/+3's partial address decoding.
/// </summary>
/// <remarks>
/// The pager is mapped twice, once per port, because its two latches have
/// different decodes: <c>0x7FFD</c> needs A14 set while <c>0x1FFD</c> needs A14
/// clear. They are therefore mutually exclusive, so a single write never reaches
/// the pager twice.
/// </remarks>
internal sealed class Plus3PortBus : IPortBus
{
    private const ushort UlaDecodeMask      = 0x0001; // A0 low
    private const ushort KempstonDecodeMask = 0x0020; // A5 low

    private readonly PortDecoder _decoder;
    private readonly Func<byte> _floatingBus;

    public Plus3PortBus(
        IPortBus ula,
        Plus3MemoryPager pager,
        Ay38912 ay,
        IPortBus? joystick,
        Func<byte> floatingBus,
        Upd765a? fdc = null)
    {
        _floatingBus = floatingBus;
        _decoder = new PortDecoder(PortDecoder.ConflictPolicy.LogicalAnd);

        // ULA: A0 low, receives the full address for keyboard scanning.
        _decoder.MapMirror(0x0000, UlaDecodeMask, 0xFFFF, ula);

        // 0x7FFD: A15 and A1 low, A14 HIGH — narrower than the 128's decode.
        _decoder.MapMirror(0x4000, 0xC002, 0xFFFF, pager);

        // 0x1FFD: A12 set, A13/A14/A15 and A1 clear.
        _decoder.MapMirror(0x1000, 0xF002, 0xFFFF, pager);

        // AY-3-8912: A1 low, A14 selects register (0xFFFD) from data (0xBFFD).
        _decoder.MapMirror(0xC000, 0xC002, 0xFFFF, ay);
        _decoder.MapMirror(0x8000, 0xC002, 0xFFFF, ay);

        // The floppy controller: status at 0x2FFD, data at 0x3FFD. Absent on a
        // +2A, which is otherwise the same machine.
        if (fdc is not null)
        {
            _decoder.MapMirror(0x2000, 0xF002, 0xFFFF, fdc);
            _decoder.MapMirror(0x3000, 0xF002, 0xFFFF, fdc);
        }

        if (joystick is not null)
        {
            _decoder.MapMirror(0x0000, KempstonDecodeMask, 0x0000, joystick);
        }
    }

    public byte In(ushort port)
    {
        _decoder.OpenBusValue = _floatingBus();
        return _decoder.In(port);
    }

    public void Out(ushort port, byte value) => _decoder.Out(port, value);
}
