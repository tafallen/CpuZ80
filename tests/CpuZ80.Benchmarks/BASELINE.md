# Benchmark Baselines

Newest first. Record the commit SHA, keep old entries so the trend stays visible.

**Read the caveat before comparing numbers across entries.** Absolute means from
different runs are *not* comparable — background load on this machine shifts a
whole run by 10% or more. Compare the **Ratio** column, which is measured within
a single run against that run's baseline benchmark, or compare the `metrics`
counts, which carry no noise at all.

Environment: .NET 8.0.419, Windows 11, 16 cores, ServerGC off.

---

## After: Tick() closed form (review item #3)

Commit: `82ffc98` + Tick rewrite. Tests: 280 passing.

`Tick` was a loop that incremented `TotalCycles` once per T-state and re-checked
the wait condition each time. Nothing in that loop could change `WaitCycles`, so
it always drained the whole pending count on its first iteration — the total was
invariably `count + WaitCycles`. Replaced with that closed form.

### Authoritative speedup — in-process A/B

Two copies of `CpuZ80.Core` differing only in `Tick`, loaded into one process via
`extern alias`, best-of-9 interleaved. This is the number to trust: both variants
run under identical thermal and scheduler conditions.

| Workload (80M T-states) | Loop | Closed form | Speedup |
|---|---:|---:|---:|
| Mixed ALU/memory | 132.69 ms (603 MHz) | 115.03 ms (696 MHz) | **1.15x** |
| NOP sled (Tick-dominated) | 80.85 ms (990 MHz) | 67.57 ms (1184 MHz) | **1.20x** |

Lower than the 1.20x/1.48x measured during the original review, because removing
`WaitPin` (item #2) had already deleted the `WaitPin ||` test from the inner loop
and banked part of the win.

### Equivalence checks

Contention metrics are bit-identical to the previous entry — 12.5% LDIR, 18.4%
CALL/RET — and the contention tests assert exact per-access durations
(`3 + pattern`), which exercise the `waits > 0` branch directly.

### CoreBenchmarks (bare CPU)

| Benchmark | Mean | Ratio |
|---|---:|---:|
| Mixed ALU/memory | 100.39 us | 1.00 |
| Register-only (Tick-dominated) | 159.71 us | 1.59 |
| LDIR block copy | 54.64 us | 0.55 |
| CALL/RET/PUSH/POP | 81.95 us | 0.82 |

### MachineBenchmarks

| Benchmark | Mean | Ratio |
|---|---:|---:|
| Spectrum RunFrame | 185.49 us | 1.00 |
| Spectrum RenderFrame | 289.66 us | 1.57 |
| Spectrum full frame | 512.32 us | 2.77 |
| Spectrum LDIR frame | 102.84 us | 0.56 |
| Spectrum stack frame | 150.98 us | 0.82 |
| ZX80 RenderFrame | 1.89 us | 0.01 |

`RunFrame` dropped 204.0 -> 185.5 us. `RenderFrame` rose 222.1 -> 289.7 us on a
run with visibly wider error bars (StdDev 41.7 us against 2.4 us in the previous
entry) — that is machine noise, not a regression: `Tick` cannot affect the video
path, and `ZX80 RenderFrame` was flat at 1.9 us. Re-measure `RenderFrame` on a
quiet machine before reading anything into it.

---

## After: route block and stack ops through ICpuHost (review item #1)

Commit: `8bca8c1` + hook fix. Tests: 262 passing (176 core, up from 165).

### Structural metrics — the result that matters

```
ICpuHost.OnMemoryAccess coverage (bus accesses vs hook calls):
  Mixed ALU/memory :   17235 accesses    17235 hooked  -> ok
  LDIR block copy  :   13321 accesses    13321 hooked  -> ok      (was 49.9% MISSED)
  CALL/RET/stack   :   19677 accesses    19677 hooked  -> ok      (was 11.4% MISSED)

Emulated throughput lost to ULA contention (code in contended RAM):
  LDIR block copy :   3332 bare    2917 hosted  ->  12.5% slower  (was ~0%)
  CALL/RET stack  :   6933 bare    5657 hosted  ->  18.4% slower  (was ~0%)

Host key queries per frame (tight IN 0xFE loop) : 15355            (unchanged — review item #6)
Allocations per full frame                      : 0 bytes          (unchanged)
```

Contention now reaches `CALL`, `RET`, `RST`, `LDIR`, `LDDR`, `CPIR` and the rest
of the block instructions. That is the point of the change; the timings below are
the price.

### MachineBenchmarks (hosted — ULA contention active)

| Benchmark | Before (ratio) | After (ratio) | Change |
|---|---:|---:|---|
| Spectrum RunFrame (baseline) | 1.002 | 1.000 | — |
| Spectrum RenderFrame | 1.113 | 1.089 | unaffected |
| Spectrum full frame | 2.240 | 2.224 | unaffected |
| **Spectrum LDIR frame** | **0.542** | **0.606** | **+12% slower** |
| **Spectrum stack frame** | **0.869** | **0.850** | no measurable change |
| ZX80 RenderFrame | 0.009 | 0.009 | unaffected |

Absolute means (before → after): RunFrame 230.7 → 204.0 us, LDIR 124.7 → 123.6 us,
stack 200.2 → 173.3 us. The whole "after" run landed ~11.6% faster on benchmarks
the change cannot touch (`RunFrame`, `Zx80RenderFrame`), so the absolute drop is
machine state, not the fix. Ratios above are the honest comparison.

**What the +12% actually is — and what it is not.** These workloads are
assembled at 0x8000, which on a 48K Spectrum is *uncontended*; only
0x4000-0x7FFF is. So the wall-clock cost above is **not** contention being
applied. It is the cost of the newly-hooked accesses calling
`UpdateFloatingBus`, which runs on every `OnMemoryAccess` regardless of address.
LDIR shows it because its hook count roughly doubled (49.9% -> 0% missed); the
stack workload's 11.4% gap is too small to surface above noise.

Review item #4 (make the floating bus lazy) should erase this regression
entirely — it removes the very work these extra hook calls are doing.

**The effect on the emulated machine** is separate and much larger. For code in
contended RAM the emulated Spectrum now correctly runs slower, which is the
whole point of the fix (see the contention figures in the metrics block above:
12.5% for LDIR, 18.4% for CALL/RET). Before the fix those numbers were ~0 —
contention simply never reached those instructions.

### CoreBenchmarks (bare CPU — no host attached, control group)

| Benchmark | Before | After | Ratio before → after |
|---|---:|---:|---|
| Mixed ALU/memory | 137.75 us | 129.14 us | 1.01 → 1.00 |
| Register-only (Tick-dominated) | 197.04 us | 173.90 us | 1.44 → 1.35 |
| LDIR block copy | 82.18 us | 75.51 us | 0.60 → 0.58 |
| CALL/RET/PUSH/POP | 122.86 us | 97.69 us | 0.90 → 0.76 |

With no host attached the hook is a predictable `if (_hasHost)` that costs
nothing measurable — as intended. This group exists to confirm the fix does not
tax machines that do not model contention.

---

## Before: performance review baseline

Commit: `8bca8c1`. Tests: 251 passing.

### Structural metrics

```
Mixed ALU/memory :   16278 reads     957 writes   11487 instructions
LDIR block copy  :    9996 reads    3325 writes    3332 instructions
CALL/RET/stack   :   14055 reads    5622 writes    6933 instructions

ICpuHost.OnMemoryAccess coverage:
  Mixed ALU/memory :   17235 accesses    17235 hooked  -> ok
  LDIR block copy  :   13321 accesses     6671 hooked  -> 6650 MISSED (49.9%)
  CALL/RET/stack   :   19677 accesses    17427 hooked  -> 2250 MISSED (11.4%)

Host key queries per frame : 15355
Allocations per full frame : 0 bytes
```

### CoreBenchmarks

| Benchmark | Mean | Ratio |
|---|---:|---:|
| Mixed ALU/memory | 137.75 us | 1.01 |
| Register-only (Tick-dominated) | 197.04 us | 1.44 |
| LDIR block copy | 82.18 us | 0.60 |
| CALL/RET/PUSH/POP | 122.86 us | 0.90 |

### MachineBenchmarks

| Benchmark | Mean | Ratio |
|---|---:|---:|
| Spectrum RunFrame | 230.74 us | 1.002 |
| Spectrum RenderFrame | 256.34 us | 1.113 |
| Spectrum full frame | 515.81 us | 2.240 |
| Spectrum LDIR frame | 124.72 us | 0.542 |
| Spectrum stack frame | 200.24 us | 0.869 |
| ZX80 RenderFrame | 2.00 us | 0.009 |

A full frame costs ~0.5 ms of a 20 ms budget (~2.6%), i.e. roughly 40x realtime.
