using System.Diagnostics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using CpuZ80.Core;
using Machines.ZxSpectrum;

namespace CpuZ80.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("metrics", StringComparison.OrdinalIgnoreCase))
        {
            Metrics.Report();
            return 0;
        }

        bool quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);
        var config = DefaultConfig.Instance.WithOptions(ConfigOptions.DisableOptimizationsValidator);
        if (quick)
        {
            config = config.AddJob(Job.ShortRun);
            args = args.Where(a => !a.Equals("--quick", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }
}

/// <summary>
/// Exact structural counts. Unlike timings these carry no measurement noise, so
/// they are the most reliable way to confirm a structural fix actually landed —
/// a lazy floating bus should drop hook calls per frame, and a snapshotted
/// keyboard should drop host queries per frame to a small constant.
/// </summary>
public static class Metrics
{
    private sealed class CountingBus : IBus
    {
        public readonly byte[] Data = new byte[0x10000];
        public long Reads, Writes;
        public byte Read(ushort address) { Reads++; return Data[address]; }
        public void Write(ushort address, byte value) { Writes++; Data[address] = value; }
    }

    private sealed class CountingHost : ICpuHost
    {
        public long MemoryAccesses, PortAccesses;
        public void OnMemoryAccess(ushort address, Cpu cpu) => MemoryAccesses++;
        public void OnPortAccess(ushort address, Cpu cpu) => PortAccesses++;
    }

    public static void Report()
    {
        Console.WriteLine("CpuZ80 structural metrics — one ZX Spectrum frame (69,888 T-states)");
        Console.WriteLine("Exact counts, no timing noise. Lower is better unless noted.");
        Console.WriteLine();

        BusTraffic("Mixed ALU/memory", Workloads.MixedAlu);
        BusTraffic("LDIR block copy ", Workloads.BlockCopy);
        BusTraffic("CALL/RET/stack  ", Workloads.StackHeavy);
        Console.WriteLine();

        HookCoverage();
        Console.WriteLine();

        ContentionThroughput();
        Console.WriteLine();

        KeyboardQueries();
        Console.WriteLine();

        FrameAllocations();
    }

    /// <summary>
    /// How much emulated throughput ULA contention costs, for code running in
    /// contended RAM. This is the visible effect of routing stack and block
    /// instructions through ICpuHost: before that fix, CALL/RET/LDIR escaped
    /// contention entirely and these figures were near zero.
    /// </summary>
    private static void ContentionThroughput()
    {
        Console.WriteLine("  Emulated throughput lost to ULA contention (code in contended RAM 0x4000-0x7FFF):");
        Console.WriteLine("    A frame is a fixed T-state budget, so wait states mean fewer instructions fit.");

        foreach (var (name, program) in new[]
                 {
                     ("LDIR block copy", Workloads.ContendedBlockCopy),
                     ("CALL/RET stack ", Workloads.ContendedStackHeavy),
                 })
        {
            long bare = CountBare(program);
            long hosted = CountHosted(program);
            double lost = (bare - hosted) * 100.0 / bare;
            Console.WriteLine($"    {name} : {bare,6} bare  {hosted,6} hosted  -> {lost,5:F1}% slower (expected: > 0)");
        }
    }

    /// <summary>Instructions per frame with no host — the uncontended reference.</summary>
    private static long CountBare(byte[] program)
    {
        var bus = new CountingBus();
        Array.Copy(program, 0, bus.Data, Workloads.ContendedOrigin, program.Length);
        var cpu = new Cpu(bus) { PC = Workloads.ContendedOrigin };

        long instructions = 0;
        ulong start = cpu.TotalCycles;
        while (cpu.TotalCycles - start < Workloads.SpectrumFrameCycles) { cpu.Step(); instructions++; }
        return instructions;
    }

    /// <summary>
    /// Instructions per frame on a real machine, stepped from reset so the frame
    /// position stays inside the ULA's visible window (T-states 14,336-57,343),
    /// which is the only span where contention is applied.
    /// </summary>
    private static long CountHosted(byte[] program)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000], audio: new NullAudioSink());
        machine.Reset();
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(Workloads.ContendedOrigin - 0x4000 + i), program[i]);
        machine.Cpu.PC = Workloads.ContendedOrigin;

        long instructions = 0;
        ulong start = machine.Cpu.TotalCycles;
        while (machine.Cpu.TotalCycles - start < Workloads.SpectrumFrameCycles) { machine.Step(); instructions++; }
        return instructions;
    }

    /// <summary>Bus traffic and instruction count for a workload on a bare CPU.</summary>
    private static void BusTraffic(string name, byte[] program)
    {
        var bus = new CountingBus();
        Array.Copy(program, 0, bus.Data, Workloads.Origin, program.Length);
        var cpu = new Cpu(bus) { PC = Workloads.Origin };

        long instructions = 0;
        ulong start = cpu.TotalCycles;
        while (cpu.TotalCycles - start < Workloads.SpectrumFrameCycles) { cpu.Step(); instructions++; }

        Console.WriteLine($"  {name} : {bus.Reads,7} reads  {bus.Writes,6} writes  {instructions,6} instructions");
    }

    /// <summary>
    /// How many bus accesses actually reach ICpuHost. Any shortfall is the
    /// block/stack bypass bug — these two numbers should be EQUAL once fixed.
    /// </summary>
    private static void HookCoverage()
    {
        Console.WriteLine("  ICpuHost.OnMemoryAccess coverage (bus accesses vs hook calls):");
        foreach (var (name, program) in new[]
                 {
                     ("Mixed ALU/memory", Workloads.MixedAlu),
                     ("LDIR block copy ", Workloads.BlockCopy),
                     ("CALL/RET/stack  ", Workloads.StackHeavy),
                 })
        {
            var bus = new CountingBus();
            Array.Copy(program, 0, bus.Data, Workloads.Origin, program.Length);
            var host = new CountingHost();
            var cpu = new Cpu(bus, null, host) { PC = Workloads.Origin };
            Workloads.RunCycles(cpu, Workloads.SpectrumFrameCycles);

            long accesses = bus.Reads + bus.Writes;
            long missed = accesses - host.MemoryAccesses;
            string verdict = missed == 0 ? "ok" : $"{missed} MISSED ({missed * 100.0 / accesses:F1}%)";
            Console.WriteLine($"    {name} : {accesses,7} accesses  {host.MemoryAccesses,7} hooked  -> {verdict}");
        }
    }

    /// <summary>Host key-state queries per frame under a tight polling loop.</summary>
    private static void KeyboardQueries()
    {
        var keyboard = new CountingKeyboard();
        var machine = new ZxSpectrumMachine(new byte[0x4000], keyboard: keyboard, audio: new NullAudioSink());
        machine.Reset();
        byte[] program = Workloads.KeyboardPoll;
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(Workloads.Origin - 0x4000 + i), program[i]);
        machine.Cpu.PC = Workloads.Origin;

        machine.RunFrame();
        keyboard.Queries = 0;
        machine.RunFrame();

        Console.WriteLine($"  Host key queries per frame (tight IN 0xFE loop) : {keyboard.Queries}");
        Console.WriteLine( "    Each is a native P/Invoke under Adapters.Raylib.");
        Console.WriteLine( "    Target after snapshotting key state once per frame: <= 40.");
    }

    /// <summary>Steady-state allocation. Currently zero — keep it that way.</summary>
    private static void FrameAllocations()
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000], audio: new NullAudioSink());
        machine.Reset();
        byte[] program = Workloads.MixedAlu;
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(Workloads.Origin - 0x4000 + i), program[i]);
        machine.Cpu.PC = Workloads.Origin;
        var sink = new NullVideoSink();

        for (int i = 0; i < 20; i++) { machine.RunFrame(); machine.RenderFrame(sink); }

        const int Frames = 200;
        long before = GC.GetAllocatedBytesForCurrentThread();
        int gcBefore = GC.CollectionCount(0);
        for (int i = 0; i < Frames; i++) { machine.RunFrame(); machine.RenderFrame(sink); }
        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / Frames;

        Console.WriteLine($"  Allocations per full frame : {perFrame} bytes  " +
                          $"(gen0 GCs over {Frames} frames: {GC.CollectionCount(0) - gcBefore})");
        Console.WriteLine( "    Emulator core is allocation-free in steady state. Any non-zero value is a regression.");
        Debug.Assert(sink.Frames > 0);
    }
}
