# CpuZ80 Benchmarks

Tracks emulator performance as the issues from the performance review are fixed.

Built on [BenchmarkDotNet](https://benchmarkdotnet.org/). Every workload is a
synthetic Z80 program (see `Workloads.cs`) — no ROM images, so results are
reproducible anywhere and nothing copyrighted is in the repo.

## Running

Full suite (slow — tens of minutes, but this is what you record):

```bash
dotnet run -c Release --project tests/CpuZ80.Benchmarks -- --filter '*'
```

One class while iterating on a fix:

```bash
dotnet run -c Release --project tests/CpuZ80.Benchmarks -- --filter '*MachineBenchmarks*'
```

Fast, low-confidence pass (3 warmup + 3 iterations) for a quick smell test:

```bash
dotnet run -c Release --project tests/CpuZ80.Benchmarks -- --filter '*' --quick
```

Structural metrics — exact counts, no timing noise, runs in seconds:

```bash
dotnet run -c Release --project tests/CpuZ80.Benchmarks -- metrics
```

## Read the metrics mode first

Timings on a loaded developer machine are noisy; during the review a single-shot
run showed `AddressDecoder` beating a raw `byte[]`, which is impossible. The
`metrics` mode has no such problem — it reports exact counts, so it is the
reliable way to confirm a structural fix actually landed:

| Metric | Meaning | Baseline | Target |
|---|---|---|---|
| Hook coverage | Bus accesses that reach `ICpuHost.OnMemoryAccess` | 0% missed | stays 0% |
| Contention throughput | Emulated slowdown for code in contended RAM | 12.5% LDIR, 18.4% CALL/RET | stays > 0% |
| Host key queries/frame | Native P/Invokes under a tight `IN 0xFE` loop | **40** (was 15,355) | ≤ 40 |
| Allocations per frame | Steady-state garbage | **0 bytes** | stays 0 |

## What each benchmark tracks

Benchmarks are written against the **public API** (`RunFrame`, `RenderFrame`)
wherever possible, so they keep compiling and stay comparable across refactors.
A lazy floating bus shows up as `SpectrumRunFrame` getting faster regardless of
how the fix is shaped.

| Tag | Finding | Benchmark |
|---|---|---|
| `TICK` | `Tick()` loops once per T-state | `CoreBenchmarks.RegisterOnly`, `.MixedAlu` |
| `HOOK` | Block/stack ops bypass `ICpuHost` | `CoreBenchmarks.BlockCopy`, `.StackHeavy` |
| `FLOAT` | `UpdateFloatingBus` per access **and** per instruction | `MachineBenchmarks.SpectrumRunFrame` |
| `BORDER` | Border pass paints 76,800 px, overwrites 49,152 | `MachineBenchmarks.SpectrumRenderFrame` |
| `FILL` | `SinclairVideo` fills whole buffer then overwrites | `MachineBenchmarks.Zx80RenderFrame` |
| `PIXEL` | Host converts ARGB→RGBA per pixel | `HostPixelBenchmarks` |
| `KEYS` | One host query per key per port read | `PeripheralBenchmarks.KeyboardPollFrame` |
| `LOCK` | Beeper takes a lock per speaker transition | `PeripheralBenchmarks.BeeperToggleFrame` |
| `ALLOC` | Zero-allocation steady state | `MemoryDiagnoser` on all machine benchmarks |
| — | Routing cost (regression guard, not a target) | `RoutingBenchmarks` |

## Two benchmarks are supposed to get slower

`CoreBenchmarks.BlockCopy` and `CoreBenchmarks.StackHeavy` currently run fast
because `Push`/`Pop` and the block instructions call `_bus.Read`/`_bus.Write`
directly, skipping `ICpuHost.OnMemoryAccess`. That is a timing-fidelity bug:
contention is never applied to `CALL`, `RET`, `LDIR` or `CPIR`.

When it is fixed those two benchmarks **will regress**, and the `metrics` hook
coverage should go to 0% missed. That trade is the point — do not "fix" the
regression by reverting it.

## Caveat on `HostPixelBenchmarks`

`ConvertArgbToRgba` duplicates the loop in `Adapters.Raylib.RaylibHost.SubmitFrame`
because that assembly needs the native Raylib binary and a window, which a
headless run cannot open. **If you change one, change the other**, or the
benchmark stops meaning anything.

## Recording a new baseline

Close other applications first; this machine showed ±40% swings under load.
Record results in `BASELINE.md` with the commit SHA, and keep the previous
entries so the trend stays visible.
