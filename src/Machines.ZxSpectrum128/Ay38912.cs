using CpuZ80.Core;

namespace Machines.ZxSpectrum128;

/// <summary>
/// General Instrument AY-3-8912 programmable sound generator, as fitted to the
/// ZX Spectrum 128 and +2.
/// </summary>
/// <remarks>
/// Three square-wave tone channels, one noise generator and one envelope
/// generator, behind 16 registers:
///
/// <code>
///  0,1  channel A tone period   (12 bits)
///  2,3  channel B tone period   (12 bits)
///  4,5  channel C tone period   (12 bits)
///  6    noise period            (5 bits)
///  7    mixer: bits 0-2 tone enable (active low), 3-5 noise enable (active low)
///  8,9,10  channel volumes      (4 bits + envelope-mode bit)
/// 11,12  envelope period        (16 bits)
/// 13     envelope shape         (4 bits)
/// 14,15  I/O ports              (the 8912 only bonds port A)
/// </code>
///
/// Ports on the 128: write 0xFFFD selects a register and reading it reads that
/// register back; writing 0xBFFD sets it. Decoded on A15, A14 and A1: both ports
/// need A1 low, then A14 picks between them — select is A15=1 A14=1 (0xFFFD),
/// data is A15=1 A14=0 (0xBFFD).
///
/// Register read-back is not a plain mirror: registers with fewer than 8
/// meaningful bits return 0 in the unused positions, which is what
/// <see cref="RegisterMask"/> models.
///
/// See docs/zx-spectrum-128.md.
/// </remarks>
public sealed class Ay38912 : IPortBus
{
    /// <summary>The AY is clocked at half the CPU clock on the Spectrum 128.</summary>
    public const int ClockHz = 1773400;

    private const int RegisterCount = 16;

    private readonly byte[] _registers = new byte[RegisterCount];

    /// <summary>Register currently selected by a write to 0xFFFD.</summary>
    public int SelectedRegister { get; private set; }

    // Per-channel square-wave state.
    private readonly int[] _toneCounter = new int[3];
    private readonly bool[] _toneOutput = new bool[3];

    /// <summary>
    /// Meaningful bits of each register. Unused positions always read back as 0
    /// regardless of what was written.
    /// </summary>
    public static byte RegisterMask(int register) => register switch
    {
        0 or 2 or 4 => 0xFF,  // tone fine
        1 or 3 or 5 => 0x0F,  // tone coarse: 4 bits
        6           => 0x1F,  // noise period: 5 bits
        7           => 0xFF,  // mixer
        8 or 9 or 10 => 0x1F, // volume: 4 bits + envelope mode
        11 or 12    => 0xFF,  // envelope period
        13          => 0x0F,  // envelope shape: 4 bits
        _           => 0xFF,  // I/O ports
    };

    public void Reset()
    {
        Array.Clear(_registers);
        Array.Clear(_toneCounter);
        Array.Clear(_toneOutput);
        SelectedRegister = 0;
    }

    // ── IPortBus ─────────────────────────────────────────────────────────────

    // A1 must be low as well as the A15/A14 pattern. Without that the chip
    // answers 0xFFFE — the "read every keyboard row" port — and corrupts the
    // keyboard read, which crashes the 128 ROM during startup.
    private const ushort AyDecodeMask = 0xC002;

    /// <summary>Register select: A15 high, A14 high, A1 low (0xFFFD).</summary>
    private static bool IsSelectPort(ushort port) => (port & AyDecodeMask) == 0xC000;

    /// <summary>Data write: A15 high, A14 low, A1 low (0xBFFD).</summary>
    private static bool IsDataPort(ushort port) => (port & AyDecodeMask) == 0x8000;

    public byte In(ushort port)
    {
        if (!IsSelectPort(port)) return 0xFF;
        return (byte)(_registers[SelectedRegister] & RegisterMask(SelectedRegister));
    }

    public void Out(ushort port, byte value)
    {
        if (IsSelectPort(port))
        {
            SelectedRegister = value & 0x0F;
            return;
        }

        if (IsDataPort(port))
        {
            _registers[SelectedRegister] = (byte)(value & RegisterMask(SelectedRegister));
        }
    }

    // ── Sound generation ─────────────────────────────────────────────────────

    /// <summary>Volume curve. The AY's steps are roughly logarithmic, ~3 dB apart.</summary>
    private static readonly int[] VolumeTable =
    [
        0, 13, 19, 28, 40, 57, 81, 115,
        162, 229, 324, 458, 647, 915, 1294, 1829
    ];

    /// <summary>
    /// Renders <paramref name="buffer"/> worth of samples covering
    /// <paramref name="tStates"/> of emulated time, and mixes the three channels
    /// into it.
    /// </summary>
    public void Render(Span<short> buffer, ulong tStates)
    {
        if (buffer.Length == 0) return;

        // AY ticks per sample. The tone counters run at the AY clock divided by
        // 16, which is the standard divider for the tone generators.
        double ayTicksPerSample = (double)tStates / buffer.Length / 16.0;

        int mixer = _registers[7];

        for (int i = 0; i < buffer.Length; i++)
        {
            int mixed = 0;

            for (int ch = 0; ch < 3; ch++)
            {
                bool toneEnabled = (mixer & (1 << ch)) == 0; // active low
                if (!toneEnabled) continue;

                int period = _registers[ch * 2] | ((_registers[ch * 2 + 1] & 0x0F) << 8);
                if (period == 0) period = 1;

                // Advance this channel's square wave over the sample window.
                _toneCounter[ch] += (int)Math.Max(1, Math.Round(ayTicksPerSample));
                while (_toneCounter[ch] >= period)
                {
                    _toneCounter[ch] -= period;
                    _toneOutput[ch] = !_toneOutput[ch];
                }

                if (!_toneOutput[ch]) continue;

                // Bit 4 selects envelope mode, which is not modelled yet; treat
                // it as full volume so envelope-driven sound is audible.
                byte volumeReg = _registers[8 + ch];
                int level = (volumeReg & 0x10) != 0 ? 0x0F : volumeReg & 0x0F;
                mixed += VolumeTable[level];
            }

            buffer[i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
        }
    }
}
