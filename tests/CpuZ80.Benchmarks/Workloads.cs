using CpuZ80.Core;
using Machines.Common;

namespace CpuZ80.Benchmarks;

/// <summary>
/// Deterministic Z80 programs used as benchmark workloads, plus the stub
/// peripherals that let a machine run headless.
/// </summary>
/// <remarks>
/// Every program is an infinite loop, so a benchmark can run it for an exact
/// number of T-states without ever falling off the end of memory. No ROM images
/// are used — the workloads are synthetic so results are reproducible on any
/// machine and nothing copyrighted enters the repository.
/// </remarks>
public static class Workloads
{
    /// <summary>
    /// Base address the workloads are assembled at: Spectrum RAM, but in the
    /// UNCONTENDED bank (0x8000+), so these measure the emulator's own cost
    /// without ULA wait states in the way.
    /// </summary>
    public const ushort Origin = 0x8000;

    /// <summary>
    /// Base address for workloads that must exercise ULA memory contention.
    /// On a 48K Spectrum only 0x4000-0x7FFF is contended — a workload at
    /// <see cref="Origin"/> never triggers it, so contention-sensitive
    /// benchmarks have to live down here.
    /// </summary>
    public const ushort ContendedOrigin = 0x6000;

    /// <summary>T-states in one ZX Spectrum 48K frame.</summary>
    public const ulong SpectrumFrameCycles = 69888;

    /// <summary>
    /// Register ALU work mixed with byte reads and writes and a conditional
    /// branch — the closest thing here to "typical" game or BASIC-interpreter code.
    /// </summary>
    public static byte[] MixedAlu =>
    [
        0x21, 0x00, 0x90,       // LD HL,0x9000
        0x11, 0x00, 0xA0,       // LD DE,0xA000
        0x01, 0x00, 0x04,       // LD BC,0x0400
        // loop:
        0x7E,                   // LD A,(HL)
        0x87,                   // ADD A,A
        0xC6, 0x37,             // ADD A,0x37
        0x12,                   // LD (DE),A
        0x23,                   // INC HL
        0x13,                   // INC DE
        0xB7,                   // OR A
        0xCB, 0x27,             // SLA A
        0x0B,                   // DEC BC
        0x78,                   // LD A,B
        0xB1,                   // OR C
        0xC2, 0x09, 0x80,       // JP NZ,loop
        0xC3, 0x00, 0x80,       // JP start
    ];

    /// <summary>
    /// Register-only work. Maximises the ratio of Tick() calls to real work, so
    /// it is the most sensitive probe for per-T-state overhead in the core.
    /// </summary>
    public static byte[] RegisterOnly =>
    [
        // loop:
        0x3C, 0x04, 0x0C, 0x14, // INC A / INC B / INC C / INC D
        0x1C, 0x24, 0x2C, 0x3D, // INC E / INC H / INC L / DEC A
        0x78, 0x81, 0x82, 0x83, // LD A,B / ADD A,C / ADD A,D / ADD A,E
        0xA8, 0xB1, 0xAF, 0x07, // XOR B / OR C / XOR A / RLCA
        0xC3, 0x00, 0x80,       // JP loop
    ];

    /// <summary>
    /// <c>LDIR</c> block copy. Exercises the block-instruction path, which
    /// currently bypasses <see cref="ICpuHost.OnMemoryAccess"/> — expect this
    /// benchmark to get SLOWER once that is corrected.
    /// </summary>
    public static byte[] BlockCopy =>
    [
        0x21, 0x00, 0x90,       // LD HL,0x9000
        0x11, 0x00, 0xA0,       // LD DE,0xA000
        0x01, 0x00, 0x08,       // LD BC,0x0800
        0xED, 0xB0,             // LDIR
        0xC3, 0x00, 0x80,       // JP start
    ];

    /// <summary>
    /// <c>CALL</c>/<c>RET</c> and <c>PUSH</c>/<c>POP</c> traffic. Exercises the
    /// stack path, which also bypasses <see cref="ICpuHost.OnMemoryAccess"/> —
    /// expect this benchmark to get SLOWER once that is corrected.
    /// </summary>
    public static byte[] StackHeavy =>
    [
        0x31, 0x00, 0xC0,       // LD SP,0xC000
        // loop:
        0xCD, 0x0D, 0x80,       // CALL sub
        0xCD, 0x0D, 0x80,       // CALL sub
        0xCD, 0x0D, 0x80,       // CALL sub
        0xC3, 0x03, 0x80,       // JP loop
        // sub: (0x800D)
        0xE5, 0xD5, 0xC5, 0xF5, // PUSH HL / PUSH DE / PUSH BC / PUSH AF
        0xF1, 0xC1, 0xD1, 0xE1, // POP AF / POP BC / POP DE / POP HL
        0xC9,                   // RET
    ];

    /// <summary>
    /// Tight <c>IN A,(0xFE)</c> keyboard polling — the shape a game's input loop
    /// takes. Drives <c>SinclairKeyboardAdapter</c>, and through it one host
    /// key-state query per key per read.
    /// </summary>
    public static byte[] KeyboardPoll =>
    [
        // loop:
        0x3E, 0xFE,             // LD A,0xFE
        0xDB, 0xFE,             // IN A,(0xFE)
        0x3E, 0xFD,             // LD A,0xFD
        0xDB, 0xFE,             // IN A,(0xFE)
        0x3E, 0x7F,             // LD A,0x7F
        0xDB, 0xFE,             // IN A,(0xFE)
        0xC3, 0x00, 0x80,       // JP loop
    ];

    /// <summary>
    /// <c>OUT (0xFE),A</c> speaker toggling — drives BeeperDevice.SetLevel, which
    /// takes a lock and appends to a list on every transition.
    /// </summary>
    public static byte[] BeeperToggle =>
    [
        // loop:
        0x3E, 0x10,             // LD A,0x10   (speaker bit set)
        0xD3, 0xFE,             // OUT (0xFE),A
        0x3E, 0x00,             // LD A,0x00   (speaker bit clear)
        0xD3, 0xFE,             // OUT (0xFE),A
        0xC3, 0x00, 0x80,       // JP loop
    ];

    /// <summary>
    /// <c>LDIR</c> copying entirely within contended RAM, assembled at
    /// <see cref="ContendedOrigin"/>. Copies 0x4000 -> 0x5000.
    /// </summary>
    public static byte[] ContendedBlockCopy =>
    [
        0x21, 0x00, 0x40,       // LD HL,0x4000
        0x11, 0x00, 0x50,       // LD DE,0x5000
        0x01, 0x00, 0x08,       // LD BC,0x0800
        0xED, 0xB0,             // LDIR
        0xC3, 0x00, 0x60,       // JP start
    ];

    /// <summary>
    /// <c>CALL</c>/<c>RET</c> with the stack itself in contended RAM, assembled
    /// at <see cref="ContendedOrigin"/>.
    /// </summary>
    public static byte[] ContendedStackHeavy =>
    [
        0x31, 0x00, 0x70,       // LD SP,0x7000  (contended)
        // loop:
        0xCD, 0x0D, 0x60,       // CALL sub
        0xCD, 0x0D, 0x60,       // CALL sub
        0xCD, 0x0D, 0x60,       // CALL sub
        0xC3, 0x03, 0x60,       // JP loop
        // sub: (0x600D)
        0xE5, 0xD5, 0xC5, 0xF5, // PUSH HL / PUSH DE / PUSH BC / PUSH AF
        0xF1, 0xC1, 0xD1, 0xE1, // POP AF / POP BC / POP DE / POP HL
        0xC9,                   // RET
    ];

    /// <summary>Builds a bare CPU over a flat 64K byte array with <paramref name="program"/> at <see cref="Origin"/>.</summary>
    public static Cpu BareCpu(byte[] program, out FlatBus bus)
    {
        bus = new FlatBus();
        Array.Copy(program, 0, bus.Data, Origin, program.Length);
        var cpu = new Cpu(bus) { PC = Origin };
        return cpu;
    }

    /// <summary>Runs <paramref name="cpu"/> until it has consumed at least <paramref name="cycles"/> T-states.</summary>
    public static void RunCycles(Cpu cpu, ulong cycles)
    {
        ulong start = cpu.TotalCycles;
        while (cpu.TotalCycles - start < cycles) cpu.Step();
    }
}

/// <summary>Flat 64K RAM with no address decoding — the routing-free reference point.</summary>
public sealed class FlatBus : IBus
{
    public readonly byte[] Data = new byte[0x10000];
    public byte Read(ushort address) => Data[address];
    public void Write(ushort address, byte value) => Data[address] = value;
}

/// <summary>Video sink that consumes frames without touching a display.</summary>
public sealed class NullVideoSink : IVideoSink
{
    public long Frames;
    public uint Checksum;

    public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
    {
        Frames++;
        // Touch the buffer so the render work cannot be optimised away.
        Checksum ^= pixels[0] ^ pixels[^1] ^ pixels[pixels.Length / 2];
    }
}

/// <summary>Audio sink that consumes samples without opening an audio device.</summary>
public sealed class NullAudioSink : IAudioSink
{
    public long Samples;
    public void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate) => Samples += samples.Length;
}

/// <summary>
/// Keyboard stub that counts queries. The real Raylib adapter answers each one
/// with a native P/Invoke, so <see cref="Queries"/> is the number that matters:
/// it is the structural cost the managed benchmark cannot show.
/// </summary>
public sealed class CountingKeyboard : IPhysicalKeyboard
{
    public long Queries;
    public bool IsKeyDown(PhysicalKey key)
    {
        Queries++;
        return false;
    }
}

/// <summary>Tape stub returning silence.</summary>
public sealed class SilentTape : ITapeDevice
{
    public bool ReadBit(ulong currentTState) => true;
    public void WriteBit(bool bit) { }
    public void Load(Stream data) { }
}
