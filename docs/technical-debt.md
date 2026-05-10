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
| TD-007 | AddressDecoder Range Granularity | Medium | **RESOLVED** | Medium |
| TD-008 | Bus Architecture: Wait State Support | Medium | **RESOLVED** | High |
| TD-009 | Port Bus Architecture: Custom Timing | Low | **RESOLVED** | Low |
| TD-010 | Test Suite Organization | Low | **RESOLVED** | Low |
| TD-011 | Register Definition Consistency | Low | **RESOLVED** | Low |
| TD-012 | Wait State Performance | Low | **RESOLVED** | Low |
| TD-013 | Sinclair Hardware Logic Redundancy | High | **RESOLVED** | Medium |
| TD-014 | CPU-Host Call Optimization | Medium | **RESOLVED** | Low |
| TD-015 | Thread-Safe Visuals & Audio | High | **RESOLVED** | Medium |
| TD-016 | Floating Bus Support | Medium | **RESOLVED** | Low |
| TD-017 | Address Mirroring Fragility | High | **RESOLVED** | Medium |
| TD-018 | Sinclair Video Heap Churn | Medium | OPEN | Low |
| TD-019 | Bus Conflict Inaccuracies | Medium | OPEN | Medium |
| TD-020 | Host Audio Sync/Latency | Low | OPEN | High |
| TD-021 | Host Audio Integration (Silent Host) | High | **RESOLVED** | Medium |
| TD-022 | Multi-byte Mode 0 Interrupts | High | **RESOLVED** | High |
| TD-023 | IXH/IXL/IYH/IYL Exposure | Low | OPEN | Low |
| TD-024 | Indexed WZ (MEMPTR) Updates | Medium | OPEN | Medium |
| TD-025 | Silicon-Accurate HALT/Refresh | Low | OPEN | Medium |
| TD-026 | NMI/INT Priority Edge-Cases | Medium | OPEN | Low |

---

## Detailed Items

### TD-017: Address Mirroring Fragility (RESOLVED)
- **Status:** Enhanced `AddressDecoder` with `MapMirror` supporting bitmask-based hardware decoding. `Zx81Machine` refactored to use this native mirroring, eliminating brittle manual loops.

### TD-021: Host Audio Integration (RESOLVED)
- **Status:** Implemented `IAudioSink` in `RaylibHost` using Raylib's `AudioStream`. Wired all Sinclair host applications to the new audio output. Move `BeeperDevice` to `Machines.Sinclair.Common` for shared usage across all Sinclair machines.

### TD-022: Multi-byte Mode 0 Interrupts (RESOLVED)
- **Status:** Refactored `Cpu.Fetch()` and `AcceptInt()` to support a "Bus-Fetch" mode. During Mode 0 interrupts, instruction operands are now correctly fetched from the data bus instead of memory.
