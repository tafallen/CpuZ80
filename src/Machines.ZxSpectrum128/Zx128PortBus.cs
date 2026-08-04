using CpuZ80.Core;

namespace Machines.ZxSpectrum128;

/// <summary>
/// Routes I/O between the ULA, the memory pager and the Kempston interface using
/// the 128's partial address decoding.
/// </summary>
/// <remarks>
/// The devices overlap deliberately. The ULA answers whenever A0 is low; the
/// pager answers whenever A15 and A1 are both low. Port 0x7FFC satisfies both,
/// and on real hardware both latch — so the decoder's LogicalAnd policy, which
/// calls every matching device, is the accurate model rather than a workaround.
/// </remarks>
internal sealed class Zx128PortBus : IPortBus
{
    private const ushort UlaDecodeMask    = 0x0001; // A0 low
    private const ushort PagerDecodeMask  = 0x8002; // A15 and A1 low
    private const ushort KempstonDecodeMask = 0x0020; // A5 low

    private readonly PortDecoder _decoder;
    private readonly FerrantiUla5C6CBridge _ulaFloatingBus;

    public Zx128PortBus(IPortBus ula, Zx128MemoryPager pager, Ay38912 ay, IPortBus? joystick, FerrantiUla5C6CBridge ulaFloatingBus)
    {
        _ulaFloatingBus = ulaFloatingBus;

        _decoder = new PortDecoder(PortDecoder.ConflictPolicy.LogicalAnd);

        // ULA: A0 low, receives the full 16-bit address for keyboard scanning.
        _decoder.MapMirror(0x0000, UlaDecodeMask, 0xFFFF, ula);

        // Memory paging latch: A15 and A1 low.
        _decoder.MapMirror(0x0000, PagerDecodeMask, 0xFFFF, pager);

        // AY-3-8912: A1 low, then A14 selects register (0xFFFD) vs data (0xBFFD).
        // A1 matters: without it the chip also answers 0xFFFE, the port a
        // program uses to scan every keyboard row at once.
        _decoder.MapMirror(0xC000, 0xC002, 0xFFFF, ay);
        _decoder.MapMirror(0x8000, 0xC002, 0xFFFF, ay);

        if (joystick is not null)
        {
            _decoder.MapMirror(0x0000, KempstonDecodeMask, 0x0000, joystick);
        }
    }

    public byte In(ushort port)
    {
        // Unclaimed ports read the ULA's floating bus.
        _decoder.OpenBusValue = _ulaFloatingBus.FloatingBusValue;
        return _decoder.In(port);
    }

    public void Out(ushort port, byte value) => _decoder.Out(port, value);
}

/// <summary>
/// Narrow view of the ULA's floating bus, so the port bus does not need to
/// depend on the whole ULA type.
/// </summary>
internal sealed class FerrantiUla5C6CBridge
{
    private readonly Func<byte> _read;
    public FerrantiUla5C6CBridge(Func<byte> read) => _read = read;
    public byte FloatingBusValue => _read();
}
