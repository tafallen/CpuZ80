# Benchmark Baselines

Newest first. Record the commit SHA, keep old entries so the trend stays visible.

**Read the caveat before comparing numbers across entries.** Absolute means from
different runs are *not* comparable — background load on this machine shifts a
whole run by 10% or more. Compare the **Ratio** column, which is measured within
a single run against that run's baseline benchmark, or compare the `metrics`
counts, which carry no noise at all.

Environment: .NET 8.0.419, Windows 11, 16 cores, ServerGC off.

---

## After: route block and stack ops through ICpuHost (review item #1)

Commit: `8bca8c1` + hook fix. Tests: 262 passing (176 core, up from 165).

### Structural metrics — the result that matters

```
ICpuHost.OnMemoryAccess coverage (bus accesses vs hook calls):
  Mixed ALU/memory :   17235 accesses    17235 hooked  -> ok
  LDIR block copy  :   13321 accesses    13321 hooked  -> ok      (was 49.9% MISSED)
  CALL/RET/stack   :   19677 accesses    19677 hooked  -> ok      (was 11.4% MISSED)

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

**Why the regression is smaller than expected:** a frame is a *fixed T-state
budget*. Applying contention makes each affected instruction consume more
T-states, so fewer instructions execute per frame. The extra hook work is
partly cancelled by there being less work to do. LDIR still shows a clear +12%;
the stack workload's cost disappears into that cancellation entirely.

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
