# Technical Debt Log - CpuZ80

This document tracks identified technical debt, architectural shortcuts, and stability issues within the CpuZ80 project.

## Summary Table

| ID | Item | Priority | Status | Complexity |
|:---|:---|:---:|:---:|:---:|
| TD-001 | Instruction Logic Divergence | High | **RESOLVED** | Medium |
| TD-002 | Exhaustive Test Stability | High | **RESOLVED** | High |
| TD-003 | T-State Granularity | High | **RESOLVED** | High |
| TD-004 | Prefix Redirection Fragility | Medium | **RESOLVED** | High |
| TD-005 | Incomplete CodeGen Migration | Medium | **RESOLVED** | Medium |

---

## Detailed Items

### TD-001: Instruction Logic Divergence (RESOLVED)
- **Status:** Consolidated all undocumented flag leakage logic into unified `SetUndocumentedFlags` helpers consumed by the CodeGen engine and manual helper methods. No logic duplication remains.

### TD-002: Exhaustive Test Stability (RESOLVED)
- **Status:** Moved the billions-of-cycles ZEXALL instruction exerciser into a standalone CLI project (`CpuZ80.Exerciser`). This bypassed .NET test-host stability issues and provided a robust, high-performance verification environment.

### TD-003: T-State Granularity (RESOLVED)
- **Status:** Refactored the entire instruction set to interleave `Tick(n)` calls with memory and I/O bus operations at the M-cycle level. The emulator now correctly models the mid-instruction timing required for hardware contention.

### TD-004: Prefix Redirection Fragility (RESOLVED)
- **Status:** Eliminated the `_indexMode` transient state machine and fragile redirection logic. Replaced with explicit, generated dispatch tables for `DD` and `FD` prefixes, achieving silicon-accurate register redirection without runtime overhead.

### TD-005: Incomplete CodeGen Migration (RESOLVED)
- **Status:** Completed the migration of 100% of the Z80 instruction set (including Block Ops, Extended, and Indexed) to the high-performance generated `switch` dispatcher. Legacy `Action[]` delegate arrays have been removed.
