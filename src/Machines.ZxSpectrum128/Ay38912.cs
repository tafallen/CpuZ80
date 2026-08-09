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

    // Noise: a 17-bit shift register tapped at bits 0 and 3.
    private int _noiseCounter;
    private int _noiseLfsr = 1;

    // Envelope: a 32-step ramp whose direction and end behaviour come from the
    // shape register.
    private int _envCounter;
    private int _envStep;
    private bool _envAttack;
    private bool _envHolding;

    /// <summary>Fractional AY ticks carried between render calls, so timing does not quantise.</summary>
    private double _tickCarry;
    private double _envTickCarry;

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

        _noiseCounter = 0;
        _noiseLfsr = 1;          // never 0: an all-zero LFSR would never move again
        _envCounter = 0;
        _envStep = 0;
        _envAttack = false;
        _envHolding = false;
        _tickCarry = 0;
        _envTickCarry = 0;
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

        // Registers 14 and 15 are the I/O ports, not storage. Register 7 bit 6
        // sets port A's direction and bit 7 port B's; when a port is an input,
        // reading its register returns the external pins rather than whatever
        // was last written. On a 128 the RS232/keypad socket is normally empty,
        // so those pins float high.
        if (SelectedRegister == 14 && (_registers[7] & 0x40) == 0) return 0xFF;
        if (SelectedRegister == 15 && (_registers[7] & 0x80) == 0) return 0xFF;

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

            // Writing the shape register restarts the envelope from the top —
            // even when the value is unchanged. Music drivers rely on this to
            // retrigger a note, so it must not be optimised into a no-op.
            if (SelectedRegister == 13) RestartEnvelope();
        }
    }

    // ── Sound generation ─────────────────────────────────────────────────────

    /// <summary>Volume curve. The AY's steps are roughly logarithmic, ~3 dB apart.</summary>
    private static readonly int[] VolumeTable =
    [
        0, 13, 19, 28, 40, 57, 81, 115,
        162, 229, 324, 458, 647, 915, 1294, 1829
    ];

    /// <summary>Current envelope output, 0-15. Exposed for tests and debugging.</summary>
    public int EnvelopeLevel => _envAttack ? _envStep >> 1 : (31 - _envStep) >> 1;

    /// <summary>Current noise output bit. Exposed for tests and debugging.</summary>
    public bool NoiseOutput => (_noiseLfsr & 1) != 0;

    /// <summary>
    /// Renders <paramref name="buffer"/> worth of samples covering
    /// <paramref name="tStates"/> of emulated time, and mixes the three channels
    /// into it.
    /// </summary>
    public void Render(Span<short> buffer, ulong tStates)
    {
        if (buffer.Length == 0) return;

        // Tone and noise counters run at the AY clock divided by 16; the
        // envelope counter divides by 256, so it steps once per 16 tone ticks.
        //
        // The fractional part of a sample's worth of ticks is carried rather
        // than rounded. Rounding to the nearest whole tick was detuning
        // everything: at 44.1 kHz a frame gives about 2.5 ticks per sample, and
        // rounding that to 3 is a 20% pitch error.
        double ticksPerSample = (double)tStates / buffer.Length / 16.0;

        int mixer = _registers[7];

        for (int i = 0; i < buffer.Length; i++)
        {
            _tickCarry += ticksPerSample;
            int ticks = (int)_tickCarry;
            _tickCarry -= ticks;

            _envTickCarry += ticksPerSample / 16.0;
            int envTicks = (int)_envTickCarry;
            _envTickCarry -= envTicks;

            AdvanceNoise(ticks);
            AdvanceEnvelope(envTicks);

            int mixed = 0;

            for (int ch = 0; ch < 3; ch++)
            {
                int period = _registers[ch * 2] | ((_registers[ch * 2 + 1] & 0x0F) << 8);
                if (period == 0) period = 1;

                _toneCounter[ch] += ticks;
                while (_toneCounter[ch] >= period)
                {
                    _toneCounter[ch] -= period;
                    _toneOutput[ch] = !_toneOutput[ch];
                }

                // Both mixer bits are active low, and the two sources are ANDed:
                // a disabled source sits high rather than silencing the channel,
                // which is how noise-only and tone-plus-noise voices work.
                bool toneDisabled  = (mixer & (1 << ch)) != 0;
                bool noiseDisabled = (mixer & (1 << (ch + 3))) != 0;
                if (toneDisabled && noiseDisabled) continue;

                bool output = (toneDisabled || _toneOutput[ch])
                           && (noiseDisabled || NoiseOutput);
                if (!output) continue;

                // Bit 4 hands the channel's amplitude to the envelope generator.
                byte volumeReg = _registers[8 + ch];
                int level = (volumeReg & 0x10) != 0 ? EnvelopeLevel : volumeReg & 0x0F;
                mixed += VolumeTable[level];
            }

            buffer[i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
        }
    }

    // ── Noise ────────────────────────────────────────────────────────────────

    private void AdvanceNoise(int ticks)
    {
        int period = _registers[6] & 0x1F;
        if (period == 0) period = 1;

        _noiseCounter += ticks;
        while (_noiseCounter >= period)
        {
            _noiseCounter -= period;

            // 17-bit maximal-length LFSR, feedback from bits 0 and 3. Taking the
            // output from bit 0 gives the AY's characteristic hiss.
            int feedback = (_noiseLfsr ^ (_noiseLfsr >> 3)) & 1;
            _noiseLfsr = (_noiseLfsr >> 1) | (feedback << 16);
        }
    }

    // ── Envelope ─────────────────────────────────────────────────────────────

    private void RestartEnvelope()
    {
        _envStep = 0;
        _envCounter = 0;
        _envAttack = (_registers[13] & 0x04) != 0;   // bit 2: ramp up rather than down
        _envHolding = false;
    }

    private void AdvanceEnvelope(int ticks)
    {
        if (ticks == 0) return;

        int period = _registers[11] | (_registers[12] << 8);
        if (period == 0) period = 1;

        _envCounter += ticks;
        while (_envCounter >= period)
        {
            _envCounter -= period;
            StepEnvelope();
        }
    }

    /// <summary>
    /// Advances one of the 32 envelope steps, applying the shape register's
    /// continue, alternate and hold bits when a ramp finishes.
    /// </summary>
    private void StepEnvelope()
    {
        if (_envHolding) return;

        _envStep++;
        if (_envStep < 32) return;

        int shape = _registers[13];
        bool cont = (shape & 0x08) != 0;
        bool alternate = (shape & 0x02) != 0;
        bool hold = (shape & 0x01) != 0;

        // Without the continue bit the envelope runs one ramp and then sits at
        // silence, whichever direction it ran — shapes 0-7 all decay to nothing.
        if (!cont)
        {
            _envHolding = true;
            _envAttack = false;
            _envStep = 31;          // level (31 - 31) >> 1 == 0
            return;
        }

        if (hold)
        {
            _envHolding = true;
            if (alternate) _envAttack = !_envAttack;
            _envStep = 31;
            return;
        }

        _envStep = 0;
        if (alternate) _envAttack = !_envAttack;
    }
}
