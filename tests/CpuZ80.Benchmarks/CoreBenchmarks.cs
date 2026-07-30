using BenchmarkDotNet.Attributes;
using CpuZ80.Core;

namespace CpuZ80.Benchmarks;

/// <summary>
/// Raw CPU throughput over a flat bus, with no machine, no ULA and no host hook.
/// Isolates the interpreter core itself.
/// </summary>
/// <remarks>
/// Each benchmark executes exactly one ZX Spectrum frame's worth of T-states
/// (69,888), so "mean" reads directly against the 20 ms real-time budget:
/// anything well under 20 ms means the core alone can outrun a real Spectrum.
///
/// Tracks review findings:
///   * TICK   — Tick() loops once per T-state (MixedAlu, RegisterOnly)
///   * HOOK   — block and stack ops bypass ICpuHost (BlockCopy, StackHeavy).
///              These two are EXPECTED TO REGRESS when that bug is fixed;
///              the extra time is correctness being paid for, not a mistake.
/// </remarks>
[MemoryDiagnoser]
public class CoreBenchmarks
{
    private Cpu _mixed = null!;
    private Cpu _registerOnly = null!;
    private Cpu _blockCopy = null!;
    private Cpu _stackHeavy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mixed        = Workloads.BareCpu(Workloads.MixedAlu, out _);
        _registerOnly = Workloads.BareCpu(Workloads.RegisterOnly, out _);
        _blockCopy    = Workloads.BareCpu(Workloads.BlockCopy, out _);
        _stackHeavy   = Workloads.BareCpu(Workloads.StackHeavy, out _);
    }

    /// <summary>Mixed ALU + memory + branches. The headline "typical code" number.</summary>
    [Benchmark(Baseline = true, Description = "Mixed ALU/memory (1 frame of T-states)")]
    public void MixedAlu() => Workloads.RunCycles(_mixed, Workloads.SpectrumFrameCycles);

    /// <summary>Register-only. Most sensitive to per-T-state overhead in Tick().</summary>
    [Benchmark(Description = "Register-only (Tick-dominated)")]
    public void RegisterOnly() => Workloads.RunCycles(_registerOnly, Workloads.SpectrumFrameCycles);

    /// <summary>LDIR block copy. Currently skips the ICpuHost hook — will slow down when fixed.</summary>
    [Benchmark(Description = "LDIR block copy [expect regression on HOOK fix]")]
    public void BlockCopy() => Workloads.RunCycles(_blockCopy, Workloads.SpectrumFrameCycles);

    /// <summary>CALL/RET/PUSH/POP. Currently skips the ICpuHost hook — will slow down when fixed.</summary>
    [Benchmark(Description = "CALL/RET/PUSH/POP [expect regression on HOOK fix]")]
    public void StackHeavy() => Workloads.RunCycles(_stackHeavy, Workloads.SpectrumFrameCycles);
}
