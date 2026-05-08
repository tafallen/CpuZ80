# Technical Debt Log - CpuZ80

This document tracks identified technical debt, architectural shortcuts, and stability issues within the CpuZ80 project.

## Summary Table

| ID | Item | Priority | Severity | Complexity |
|:---|:---|:---:|:---:|:---:|
| TD-001 | Instruction Logic Divergence | High | Major | Medium |
| TD-002 | Exhaustive Test Stability | High | Major | High |
| TD-003 | T-State Granularity | High | Major | High |
| TD-004 | Prefix Redirection Fragility | Medium | Major | High |
| TD-005 | Incomplete CodeGen Migration | Medium | Minor | Medium |

---

## Detailed Items

### TD-001: Instruction Logic Divergence
- **Description:** Undocumented flag leakage logic (e.g., bit 3/5 leaks from MEMPTR or Accumulator) is duplicated across the `CpuZ80.CodeGen` project and manual implementation files (`Cpu.Bitwise.cs`, `Cpu.Extended.cs`).
- **Risk:** High chance of behavioral drift or bugs when updating flag logic, as changes must be manually synced across three architectural layers.
- **Remediation:** Consolidate all flag update rules into the CodeGen metadata or a shared static helper class that both the generator and manual instructions consume.

### TD-002: Exhaustive Test Stability
- **Description:** The .NET test host crashes during the exhaustive ZEXALL integration test (simulating >5.7 billion cycles).
- **Risk:** Inability to perform 100% reliable regression testing for subtle CPU core changes.
- **Remediation:** Move long-running exercisers out of xUnit into a dedicated CLI project (`CpuZ80.Exerciser`) that runs in a more stable environment and can handle billions of cycles without test-host overhead.

### TD-003: T-State Granularity
- **Description:** While `Tick(n)` exists, many manual instructions (Block Operations, Extended) bulk-update cycles at the end of the method rather than interleaving them with memory access.
- **Risk:** Will block accurate emulation of the ZX Spectrum ULA memory contention, which requires pausing the CPU *during* specific T-states of a memory fetch.
- **Remediation:** Audit all instructions and move `Tick` calls to occur precisely alongside `Read`/`Write`/`Fetch` operations.

### TD-004: Prefix Redirection Fragility
- **Description:** The `_evaluatingHlPtr` flag and the `GetReg`/`SetReg` redirection logic (converting `H/L` to `IXH/IXL` only when `(IX+d)` is not used) is complex and difficult to maintain.
- **Risk:** Subtle bugs in undocumented prefix behavior; high cognitive load for future contributors.
- **Remediation:** Evaluate migrating to fully generated instruction tables for `DD` and `FD` prefixes, eliminating the transient state pattern for redirection.

### TD-005: Incomplete CodeGen Migration
- **Description:** Approximately 40% of instructions (Block Ops, Bitwise Shifts, Extended) are still dispatched via the slower `Action[]` delegate array.
- **Risk:** Performance bottleneck in high-load emulation scenarios.
- **Remediation:** Complete the migration of all instruction sets to the CodeGen `switch` dispatcher.
