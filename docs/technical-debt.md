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
| TD-006 | CodeGen Code Quality (Warnings) | Low | **RESOLVED** | Low |
| TD-007 | AddressDecoder Range Granularity | Medium | Open | Medium |
| TD-008 | Bus Architecture: Wait State Support | Medium | **RESOLVED** | High |
| TD-009 | Port Bus Architecture: Custom Timing | Low | **RESOLVED** | Low |
| TD-010 | Test Suite Organization | Low | **RESOLVED** | Low |

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

### TD-006: CodeGen Code Quality (RESOLVED)
- **Status:** Suppressed localized `CS0162` warnings in `Cpu.Generated.cs` and cleaned up constant condition checks (like `if (6 != 6)`) in the Code Generator. The project now builds with **0 warnings**.

### TD-007: AddressDecoder Range Granularity (OPEN)
- **Description:** `AddressDecoder` requires 256-byte page alignment for all mappings.
- **Risk:** Limits accuracy for machines with sub-page memory-mapped I/O or small ROM/RAM mirrors.
- **Remediation:** Support a secondary "Fine-Grained Mappings" list for sub-page addresses.

### TD-008: Bus Architecture: Wait State Support (RESOLVED)
- **Status:** Implemented a `WaitPin` property on the `Cpu` and updated the `Tick(n)` mechanism to poll this pin during every T-state. This allows hardware components to pause the CPU mid-instruction for cycle-perfect synchronization.

### TD-009: Port Bus Architecture: Custom Timing (RESOLVED)
- **Status:** Abstracted I/O timing into a `PortTick(ushort port)` helper. This allows machine implementations (like the ZX Spectrum ULA) to add custom contention or wait-states to specific I/O address ranges without modifying the CPU core.

### TD-010: Test Suite Organization (RESOLVED)
- **Status:** Reorganized `tests/CpuZ80.Tests` into a logical directory structure (Arithmetic, Bitwise, ControlFlow, Hardware, etc.) matching the core project's modular design.
