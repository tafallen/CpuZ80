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
| TD-018 | Sinclair Video Heap Churn | Medium | **RESOLVED** | Low |
| TD-019 | Bus Conflict Inaccuracies | Medium | **RESOLVED** | Medium |
| TD-020 | Host Audio Sync/Latency | Low | OPEN | High |
| TD-021 | Host Audio Integration (Silent Host) | High | **RESOLVED** | Medium |
| TD-022 | Multi-byte Mode 0 Interrupts | High | **RESOLVED** | High |
| TD-023 | IXH/IXL/IYH/IYL Exposure | Low | OPEN | Low |
| TD-024 | Indexed WZ (MEMPTR) Updates | Medium | **RESOLVED** | Medium |
| TD-025 | Silicon-Accurate HALT/Refresh | Low | OPEN | Medium |
| TD-026 | NMI/INT Priority Edge-Cases | Medium | **RESOLVED** | Low |
| TD-027 | AddressMask Validation | Medium | **RESOLVED** | Low |
| TD-028 | Mirroring Setup Optimization | Low | OPEN | Medium |
| TD-029 | Hard-coded PWM Timings | Low | OPEN | Medium |
| TD-030 | Robust TAP Parsing | Medium | **RESOLVED** | Low |
| TD-031 | Pilot Pulse Heuristics | Low | OPEN | Low |
| TD-032 | Tape Interface Pollution | Low | OPEN | Low |
| TD-033 | PWM Logic Fragmentation | Medium | OPEN | Medium |
| TD-034 | Semantic Decoding Constants | Low | OPEN | Low |
| TD-035 | Mapping Struct Cache Locality | Low | OPEN | Medium |
| TD-036 | High-Fidelity Floating Bus | Medium | **RESOLVED** | Medium |
| TD-037 | CodeGen Monolithic Design | Low | OPEN | Medium |

---

## Detailed Items

### TD-015: Thread-Safe Visuals & Audio (RESOLVED)
- **Status:** Implemented a double-buffering mechanism for both border and audio transitions. `ZxSpectrumMachine` now snapshots hardware state at the start of a frame, ensuring the UI rendering thread has a stable, immutable data set while the emulation thread continues in the background. This eliminates `InvalidOperationException` and visual flickering.

### TD-016: Floating Bus Support (RESOLVED)
- **Status:** Enhanced `ZxSpectrumPortBus` to support the iconic Spectrum "Floating Bus" behavior. Reads from unmapped I/O ports now return the ULA's currently processed attribute byte instead of a static `0xFF`, a critical requirement for certain copy-protection and visual effect routines.

### TD-017: Address Mirroring Fragility (RESOLVED)
- **Status:** Enhanced `AddressDecoder` with `MapMirror` supporting bitmask-based hardware decoding. `Zx81Machine` refactored to use this native mirroring, eliminating brittle manual loops.

### TD-018: Sinclair Video Heap Churn (RESOLVED)
- **Status:** Refactored `SinclairVideo.Render` to use a pre-allocated row buffer. This eliminates 1,200 allocations per second during rendering, significantly reducing garbage collection pressure.

### TD-019: Bus Conflict Inaccuracies (RESOLVED)
- **Status:** Upgraded `AddressDecoder` to support custom bus conflict resolution policies. Implemented `ConflictPolicy.LogicalAnd` which correctly models physical bus contention by AND-ing data from multiple responding devices.

### TD-021: Host Audio Integration (RESOLVED)
- **Status:** Implemented `IAudioSink` in `RaylibHost` using Raylib's `AudioStream`. Wired all Sinclair host applications to the new audio output. Move `BeeperDevice` to `Machines.Sinclair.Common` for shared usage across all Sinclair machines.

### TD-022: Multi-byte Mode 0 Interrupts (RESOLVED)
- **Status:** Refactored `Cpu.Fetch()` and `AcceptInt()` to support a "Bus-Fetch" mode. During Mode 0 interrupts, instruction operands are now correctly fetched from the data bus instead of memory.

### TD-024: Indexed WZ (MEMPTR) Updates (RESOLVED)
- **Status:** Updated the `CpuZ80.CodeGen` and `Cpu.Bitwise.cs` to correctly handle MEMPTR (WZ) updates during indexed bitwise instructions. Specifically, `BIT n, (IX+d)` now correctly updates undocumented flags based on bits 11 and 13 of the effective address (WZ).

### TD-026: NMI/INT Priority Edge-Cases (RESOLVED)
- **Status:** Added an exhaustive test suite (`InterruptPriorityTests.cs`) covering priority between simultaneous NMI and INT signals, as well as HALT-exit edge cases. Confirmed correct Z80 behavior for interrupt enabling/disabling states.

### TD-027: AddressMask Validation (RESOLVED)
- **Status:** Enhanced `AddressDecoder.MapMirror` with validation logic to ensure provided bitmasks align with the mapped device's physical capacity (RAM/ROM size). This prevents silent memory wrapping and corruption bugs.

### TD-030: Robust TAP Parsing (RESOLVED)
- **Status:** Hardened the `ZxSpectrumTapeAdapter` with explicit block length validation and descriptive error reporting. This ensures the emulator handles corrupted or truncated .TAP files gracefully.

### TD-036: High-Fidelity Floating Bus (RESOLVED)
- **Status:** Implemented real-time attribute sampling in the `FerrantiUla5C6C`. Unmapped port reads now return the actual attribute byte currently being fetched by the ULA based on the precise T-state within the scanline.
