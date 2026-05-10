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

---

## Detailed Items

### TD-015: Thread-Safe Visuals & Audio (RESOLVED)
- **Status:** Implemented a double-buffering mechanism for both border and audio transitions. `ZxSpectrumMachine` now snapshots hardware state at the start of a frame, ensuring the UI rendering thread has a stable, immutable data set while the emulation thread continues in the background. This eliminates `InvalidOperationException` and visual flickering.

### TD-016: Floating Bus Support (RESOLVED)
- **Status:** Enhanced `ZxSpectrumPortBus` to support the iconic Spectrum "Floating Bus" behavior. Reads from unmapped I/O ports now return the ULA's currently processed attribute byte instead of a static `0xFF`, a critical requirement for certain copy-protection and visual effect routines.
