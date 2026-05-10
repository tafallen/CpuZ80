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
| TD-017 | Address Mirroring Fragility | High | OPEN | Medium |
| TD-018 | Sinclair Video Heap Churn | Medium | OPEN | Low |
| TD-019 | Bus Conflict Inaccuracies | Medium | OPEN | Medium |
| TD-020 | Host Audio Sync/Latency | Low | OPEN | High |
| TD-021 | Host Audio Integration (Silent Host) | High | OPEN | Medium |
| TD-022 | Multi-byte Mode 0 Interrupts | High | OPEN | High |
| TD-023 | IXH/IXL/IYH/IYL Exposure | Low | OPEN | Low |
| TD-024 | Indexed WZ (MEMPTR) Updates | Medium | OPEN | Medium |
| TD-025 | Silicon-Accurate HALT/Refresh | Low | OPEN | Medium |
| TD-026 | NMI/INT Priority Edge-Cases | Medium | OPEN | Low |

---

## Detailed Items

### TD-015: Thread-Safe Visuals & Audio (RESOLVED)
- **Status:** Implemented a double-buffering mechanism for both border and audio transitions. `ZxSpectrumMachine` now snapshots hardware state at the start of a frame, ensuring the UI rendering thread has a stable, immutable data set while the emulation thread continues in the background. This eliminates `InvalidOperationException` and visual flickering.

### TD-016: Floating Bus Support (RESOLVED)
- **Status:** Enhanced `ZxSpectrumPortBus` to support the iconic Spectrum "Floating Bus" behavior. Reads from unmapped I/O ports now return the ULA's currently processed attribute byte instead of a static `0xFF`, a critical requirement for certain copy-protection and visual effect routines.

### TD-017: Address Mirroring Fragility (OPEN)
- **Issue:** Memory mirroring (e.g., ZX81 1K mirrors) is currently implemented using manual loops in machine constructors.
- **Risk:** High maintenance overhead and error-prone during machine configuration changes (like RAM expansions).
- **Remediation:** Enhance `AddressDecoder` to support bitmask-based mirroring and partial decoding natively.

### TD-018: Sinclair Video Heap Churn (OPEN)
- **Issue:** `SinclairVideo.Render` allocates a new `rowCodes` byte array for every character row, generating over 1,200 allocations per second.
- **Risk:** Unnecessary garbage collection pressure and micro-stutters.
- **Remediation:** Refactor to use a single pre-allocated row buffer or process RAM slices directly.

### TD-019: Bus Conflict Inaccuracies (OPEN)
- **Issue:** `AddressDecoder` uses "Last-Registration-Wins" for overlapping ranges.
- **Risk:** Inaccurate for complex hardware where multiple devices might drive the data bus simultaneously (typically resulting in a Logical AND).
- **Remediation:** Implement support for bus conflict resolution policies (e.g., `ConflictPolicy.LogicalAnd`).

### TD-020: Host Audio Sync/Latency (OPEN)
- **Issue:** No synchronization mechanism between the Z80 audio generation rate and the host's physical sound card clock.
- **Risk:** Audio "pops," "crackles," or long-term drift during extended sessions.
- **Remediation:** Implement a host-clock synchronization hook (`SyncToHostClock`) to regulate emulation speed based on audio buffer pressure.

### TD-021: Host Audio Integration (OPEN)
- **Issue:** `IAudioSink` is defined and used by machines, but `Adapters.Raylib` does not implement it, and hosts do not initialize audio hardware.
- **Risk:** The emulator is currently silent despite having high-fidelity beeper emulation.
- **Remediation:** Implement `IAudioSink` in `RaylibHost` using Raylib's `AudioStream` and wire it into `Host.Zx80`, `Host.Zx81`, and `Host.ZxSpectrum`.

### TD-022: Multi-byte Mode 0 Interrupts (OPEN)
- **Issue:** `AcceptInt` for Mode 0 only supports single-byte instructions. Multi-byte instructions (like `CALL nn`) fetch operands from PC instead of the bus.
- **Risk:** Incorrect behavior for hardware providing complex interrupt instructions.
- **Remediation:** Refactor `AcceptInt` to support a "Bus-Fetch" mode where subsequent operand fetches are redirected to the I/O bus.

### TD-023: IXH/IXL/IYH/IYL Exposure (OPEN)
- **Issue:** The index half-registers are private/internal.
- **Risk:** Poor observability and difficulty implementing snapshot saving/loading or debuggers.
- **Remediation:** Expose these as public properties in `Cpu.cs`.

### TD-024: Indexed WZ (MEMPTR) Updates (OPEN)
- **Issue:** `TransformToIndexed` in `CpuZ80.CodeGen` does not yet account for subtle MEMPTR updates during bitwise instructions (e.g. `BIT n, (IX+d)`).
- **Risk:** Incorrect undocumented flag behavior for indexed instructions.
- **Remediation:** Update the generator transformation logic to include correct WZ assignments for all indexed patterns.

### TD-025: Silicon-Accurate HALT/Refresh (OPEN)
- **Issue:** `HALT` implementation is a simple instruction-level loop.
- **Risk:** Inaccurate R-register increment timing and refresh cycle behavior compared to real silicon.
- **Remediation:** Model HALT as a series of 4-cycle refresh loops.

### TD-026: NMI/INT Priority Edge-Cases (OPEN)
- **Issue:** Priority between simultaneous NMI and INT signals needs rigorous verification, especially regarding the `IFF2` state save.
- **Risk:** Potential edge-case bugs in complex OS ROMs.
- **Remediation:** Add exhaustive unit tests for NMI/INT contention and HALT exit priority.
