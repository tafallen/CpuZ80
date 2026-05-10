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
| TD-027 | AddressMask Validation | Medium | OPEN | Low |
| TD-028 | Mirroring Setup Optimization | Low | OPEN | Medium |
| TD-029 | Hard-coded PWM Timings | Low | OPEN | Medium |
| TD-030 | Robust TAP Parsing | Medium | OPEN | Low |
| TD-031 | Pilot Pulse Heuristics | Low | OPEN | Low |
| TD-032 | Tape Interface Pollution | Low | OPEN | Low |
| TD-033 | PWM Logic Fragmentation | Medium | OPEN | Medium |
| TD-034 | Semantic Decoding Constants | Low | OPEN | Low |
| TD-035 | Mapping Struct Cache Locality | Low | OPEN | Medium |
| TD-036 | High-Fidelity Floating Bus | Medium | OPEN | Medium |
| TD-037 | CodeGen Monolithic Design | Low | OPEN | Medium |

---

## Detailed Items

### TD-015: Thread-Safe Visuals & Audio (RESOLVED)
- **Status:** Implemented a double-buffering mechanism for both border and audio transitions. `ZxSpectrumMachine` now snapshots hardware state at the start of a frame, ensuring the UI rendering thread has a stable, immutable data set while the emulation thread continues in the background. This eliminates `InvalidOperationException` and visual flickering.

### TD-016: Floating Bus Support (RESOLVED)
- **Status:** Enhanced `ZxSpectrumPortBus` to support the iconic Spectrum "Floating Bus" behavior. Reads from unmapped I/O ports now return the ULA's currently processed attribute byte instead of a static `0xFF`, a critical requirement for certain copy-protection and visual effect routines.

### TD-017: Address Mirroring Fragility (RESOLVED)
- **Status:** Enhanced `AddressDecoder` with `MapMirror` supporting bitmask-based hardware decoding. `Zx81Machine` refactored to use this native mirroring, eliminating brittle manual loops.

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

### TD-021: Host Audio Integration (RESOLVED)
- **Status:** Implemented `IAudioSink` in `RaylibHost` using Raylib's `AudioStream`. Wired all Sinclair host applications to the new audio output. Move `BeeperDevice` to `Machines.Sinclair.Common` for shared usage across all Sinclair machines.

### TD-022: Multi-byte Mode 0 Interrupts (RESOLVED)
- **Status:** Refactored `Cpu.Fetch()` and `AcceptInt()` to support a "Bus-Fetch" mode. During Mode 0 interrupts, instruction operands are now correctly fetched from the data bus instead of memory.

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

### TD-027: AddressMask Validation (OPEN)
- **Issue:** `AddressDecoder.MapMirror` relies on the caller providing a correct bitmask. Invalid masks could cause silent data corruption or out-of-bounds access.
- **Risk:** Unsafe memory mapping API.
- **Remediation:** Add validation to ensure the provided addressMask correctly aligns with the mapped device's capacity.

### TD-028: Mirroring Setup Optimization (OPEN)
- **Issue:** `MapMirror` performs an O(N) scan of the 64KB lookup table for every device.
- **Impact:** Slow machine initialization and potential bottleneck for high-frequency banking.
- **Remediation:** Optimize initialization logic to target only relevant table indices.

### TD-029: Hard-coded PWM Timings (OPEN)
- **Issue:** Pulse durations (Pilot, Sync, Bit 0/1) are hard-coded as constants in `ZxSpectrumTapeAdapter`.
- **Risk:** Inflexible for non-standard "Turbo" loaders or the future implementation of the highly variable **.TZX** format.
- **Remediation:** Refactor pulse generation to be data-driven/configurable per block.

### TD-030: Robust TAP Parsing (OPEN)
- **Issue:** The `.TAP` parser assumes well-formed input and lacks block length validation.
- **Risk:** Corrupted files could cause `EndOfStreamException` or silent data truncation.
- **Remediation:** Add explicit length checks and descriptive error reporting to the `Load` method.

### TD-031: Pilot Pulse Heuristics (OPEN)
- **Issue:** Pilot pulse count (Header vs Data) is derived from a simple flag byte check (`flag < 128`).
- **Impact:** May fail for obscure or non-standard tape images that deviate from the Sinclair spec.
- **Remediation:** Implement more robust block-type tracking during parsing.

### TD-032: Tape Interface Pollution (OPEN)
- **Issue:** `ITapeDevice.ReadBit` now requires a T-state parameter even for machines (ZX80/ZX81) that only use pulse-counting logic.
- **Impact:** Forces "least common multiple" complexity onto simpler machine implementations.
- **Remediation:** Potentially introduce a `IClockedTapeDevice` specialization.

### TD-033: PWM Logic Fragmentation (OPEN)
- **Issue:** `SinclairTapeAdapter` (pulse-count) and `ZxSpectrumTapeAdapter` (PWM) live in different projects and namespaces.
- **Future Impact:** Implementation of **.CDT** (Amstrad) will lead to a third redundant implementation.
- **Remediation:** Consolidate PWM bitstreaming logic into a reusable generic Sinclair-family component.

### TD-034: Semantic Decoding Constants (OPEN)
- **Issue:** Machine compositors use literal masks (e.g. `0xC000`) for hardware decoding.
- **Risk:** Poor readability; masks don't explain which address lines are being decoded.
- **Remediation:** Replace magic numbers with semantic constants (e.g. `const ushort A14_A15_Mask = 0xC000`).

### TD-035: Mapping Struct Cache Locality (OPEN)
- **Issue:** The `AddressDecoder.Mapping` struct size has increased to support mirroring/masks.
- **Impact:** Increased memory footprint (~1MB) may cause more L2 cache misses during performance-critical bus operations.
- **Remediation:** Profile and potentially optimize the struct layout or use a separate bitmask array.

### TD-036: High-Fidelity Floating Bus (OPEN)
- **Issue:** The current Floating Bus implementation is a stub returning a static value.
- **Risk:** Inaccurate for games that rely on the ULA's attribute-fetch timing.
- **Remediation:** Implement real-time attribute sampling based on the ULA's scanline position.

### TD-037: CodeGen Monolithic Design (OPEN)
- **Issue:** `CpuZ80.CodeGen/Program.cs` uses a large monolithic method for instruction generation.
- **Risk:** Hard to maintain and extend for complex new instructions (e.g. multi-byte Mode 0 interrupts).
- **Remediation:** Refactor the generator into smaller, specialized transformation classes.
