using BenchmarkDotNet.Attributes;
using CpuZ80.Core;

namespace CpuZ80.Benchmarks;

/// <summary>
/// Memory routing cost: <see cref="AddressDecoder"/> against a routing-free flat
/// array, both under realistic CPU execution and under adversarial random access.
/// </summary>
/// <remarks>
/// Mostly a REGRESSION GUARD rather than a target. The review measured the
/// decoder at only ~1.03x the cost of a flat array under real code — Z80
/// programs have strong address locality, so the routing table stays hot in
/// cache. The random-access pair below is the adversarial case (~2x) and exists
/// to show what the decoder would cost if a future machine ever started
/// scattering its accesses.
///
/// Keep an eye on the gap between DecoderExecution and FlatBusExecution. If it
/// widens materially after a change, routing has become a real cost.
///
/// <see cref="BankSwitch16K"/> IS a target. Machines that page memory at runtime
/// (Spectrum 128K, Amstrad CPC, MSX) do this constantly, and the old flat
/// 65,536-entry table made it O(bytes) — ~90 us to swap a 16K window. It is now
/// O(pages).
/// </remarks>
public class RoutingBenchmarks
{
    private Cpu _decoderCpu = null!;
    private Cpu _flatCpu = null!;
    private AddressDecoder _decoder = null!;
    private byte[] _raw = null!;
    private IBus[] _banks = null!;

    private const int RandomAccesses = 1_000_000;

    [GlobalSetup]
    public void Setup()
    {
        // Decoder-backed CPU: ROM low, RAM high, as a real machine maps it.
        var ram = new Ram(0x10000);
        _decoder = new AddressDecoder();
        _decoder.Map(0x0000, 0xFFFF, ram);
        byte[] program = Workloads.MixedAlu;
        for (int i = 0; i < program.Length; i++)
            ram.Write((ushort)(Workloads.Origin + i), program[i]);
        _decoderCpu = new Cpu(_decoder) { PC = Workloads.Origin };
        _raw = ram.RawBytes;

        _flatCpu = Workloads.BareCpu(Workloads.MixedAlu, out _);

        // Four 16K banks to page through the top of the address space.
        _banks = [new Ram(0x4000), new Ram(0x4000), new Ram(0x4000), new Ram(0x4000)];
    }

    /// <summary>Real code through the decoder — the case that actually matters.</summary>
    [Benchmark(Baseline = true, Description = "Execution via AddressDecoder")]
    public void DecoderExecution() => Workloads.RunCycles(_decoderCpu, Workloads.SpectrumFrameCycles);

    /// <summary>The same code with no routing at all — the floor.</summary>
    [Benchmark(Description = "Execution via flat byte[] (no routing)")]
    public void FlatBusExecution() => Workloads.RunCycles(_flatCpu, Workloads.SpectrumFrameCycles);

    /// <summary>Adversarial: scattered reads that defeat the routing table's cache locality.</summary>
    [Benchmark(Description = "Random reads via AddressDecoder")]
    public uint DecoderRandomReads()
    {
        uint seed = 1, acc = 0;
        for (int i = 0; i < RandomAccesses; i++)
        {
            seed = seed * 1664525 + 1013904223;
            acc += _decoder.Read((ushort)(seed >> 16));
        }
        return acc;
    }

    /// <summary>The same scattered reads straight off the array.</summary>
    [Benchmark(Description = "Random reads via raw byte[]")]
    public uint RawRandomReads()
    {
        uint seed = 1, acc = 0;
        for (int i = 0; i < RandomAccesses; i++)
        {
            seed = seed * 1664525 + 1013904223;
            acc += _raw[(ushort)(seed >> 16)];
        }
        return acc;
    }

    /// <summary>
    /// Paging a 16K window, as a Spectrum 128K or CPC does at runtime. Must stay
    /// proportional to the 64 pages in the window, not the 16,384 addresses.
    /// </summary>
    [Benchmark(Description = "Bank-switch a 16K window x1000 [target]")]
    public void BankSwitch16K()
    {
        for (int i = 0; i < 1000; i++)
        {
            _decoder.Remap(0xC000, 0xFFFF, _banks[i & 3]);
        }
    }
}
