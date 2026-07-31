using BenchmarkDotNet.Attributes;
using Machines.Zx80;
using Machines.ZxSpectrum;

namespace CpuZ80.Benchmarks;

/// <summary>
/// Whole-machine benchmarks at the public API — <c>RunFrame()</c> and
/// <c>RenderFrame()</c>. These are the numbers that decide whether the emulator
/// keeps up with a real machine.
/// </summary>
/// <remarks>
/// Deliberately measured through the public surface rather than at internal
/// methods, so they keep compiling and stay comparable across the refactors in
/// the review. A fix that makes the floating bus lazy shows up here as
/// <c>SpectrumRunFrame</c> getting faster, whatever shape the fix takes.
///
/// Budget: one frame at 50 Hz is 20 ms. RunFrame + RenderFrame must fit inside
/// that with room for the host's own work.
///
/// Tracks review findings:
///   * FLOAT  — floating bus sampling cost (SpectrumRunFrame). Fixed in the
///              lazy-floating-bus change; this guards against it going eager again.
///   * BORDER — border pass paints 76,800 px then overwrites 49,152 (SpectrumRenderFrame)
///   * FILL   — SinclairVideo fills the whole buffer then overwrites (Zx80RenderFrame)
///   * ALLOC  — MemoryDiagnoser guards the current zero-allocation steady state
/// </remarks>
[MemoryDiagnoser]
public class MachineBenchmarks
{
    private ZxSpectrumMachine _spectrum = null!;
    private ZxSpectrumMachine _spectrumBlockCopy = null!;
    private ZxSpectrumMachine _spectrumStack = null!;
    private Zx80Machine _zx80 = null!;
    private NullVideoSink _video = null!;
    private NullAudioSink _audio = null!;

    /// <summary>
    /// Builds a Spectrum running <paramref name="program"/> from RAM. Spectrum RAM
    /// is mapped at 0x4000, so the backing-array index is (address - 0x4000).
    /// </summary>
    private ZxSpectrumMachine BuildSpectrum(byte[] program)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000], audio: _audio);
        machine.Reset();
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(Workloads.Origin - 0x4000 + i), program[i]);
        machine.Cpu.PC = Workloads.Origin;
        return machine;
    }

    [GlobalSetup]
    public void Setup()
    {
        _video = new NullVideoSink();
        _audio = new NullAudioSink();

        // Synthetic ROM of NOPs — no copyrighted image, fully deterministic.
        _spectrum = BuildSpectrum(Workloads.MixedAlu);

        // Host-attached block/stack workloads. CoreBenchmarks runs these on a
        // BARE CPU where _hasHost is false, so the ICpuHost bypass is invisible
        // there — contention only reaches them through a real machine.
        _spectrumBlockCopy = BuildSpectrum(Workloads.BlockCopy);
        _spectrumStack     = BuildSpectrum(Workloads.StackHeavy);

        // Give the display something non-uniform to render, so the video path
        // is not measured against an all-zero screen.
        var rnd = new Random(20260729);
        for (ushort i = 0; i < 0x1B00; i++)
            _spectrum.Ram.Write(i, (byte)rnd.Next(256));

        _zx80 = new Zx80Machine(new byte[0x1000], audio: _audio);
        _zx80.Reset();

        // Warm every machine so first-call JIT is not inside a measurement.
        for (int i = 0; i < 3; i++)
        {
            _spectrum.RunFrame();
            _spectrum.RenderFrame(_video);
            _spectrumBlockCopy.RunFrame();
            _spectrumStack.RunFrame();
            _zx80.RunFrame();
            _zx80.RenderFrame(_video);
        }
    }

    /// <summary>Emulating one Spectrum frame: CPU + ULA contention + floating bus.</summary>
    [Benchmark(Baseline = true, Description = "Spectrum RunFrame (emulation)")]
    public void SpectrumRunFrame() => _spectrum.RunFrame();

    /// <summary>Rendering one Spectrum frame: border pass, bitmap+attributes, beeper resample.</summary>
    [Benchmark(Description = "Spectrum RenderFrame (video+audio)")]
    public void SpectrumRenderFrame() => _spectrum.RenderFrame(_video);

    /// <summary>The number that must stay under 20 ms.</summary>
    [Benchmark(Description = "Spectrum full frame (run+render, 20ms budget)")]
    public void SpectrumFullFrame()
    {
        _spectrum.RunFrame();
        _spectrum.RenderFrame(_video);
    }

    /// <summary>
    /// LDIR block copy under a real ULA host. This is where the ICpuHost bypass
    /// actually costs fidelity — half of LDIR's bus traffic never reaches
    /// contention. EXPECT THIS TO REGRESS when that is fixed.
    /// </summary>
    [Benchmark(Description = "Spectrum LDIR frame (hosted) [expect regression on HOOK fix]")]
    public void SpectrumBlockCopyFrame() => _spectrumBlockCopy.RunFrame();

    /// <summary>
    /// CALL/RET/PUSH/POP under a real ULA host. EXPECT THIS TO REGRESS when the
    /// ICpuHost bypass is fixed.
    /// </summary>
    [Benchmark(Description = "Spectrum stack frame (hosted) [expect regression on HOOK fix]")]
    public void SpectrumStackFrame() => _spectrumStack.RunFrame();

    /// <summary>ZX80 character-cell rendering via SinclairVideo.</summary>
    [Benchmark(Description = "ZX80 RenderFrame (SinclairVideo)")]
    public void Zx80RenderFrame() => _zx80.RenderFrame(_video);
}
