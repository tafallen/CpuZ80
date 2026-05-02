# Z80 Emulator Architectural Critique and Improvement Opportunities

## Strengths and Elegant Design Choices

1.  **The `IndexMode` State Machine:** The architecture uses a transient state pattern (`_indexMode`) for handling Z80 prefixes (`0xDD`, `0xFD`). By temporarily swapping `HL` for `IX/IY` within context-aware `GetReg`/`SetReg` methods, the emulator achieves high code reuse and makes adding undocumented `IXH/IXL` instructions trivial without duplicating the entire base instruction set.
2.  **Tiered Dispatch Tables:** The use of `Action[]` arrays for dispatching (`_ops`, `_cbOps`, `_edOps`) provides a clean, modular structure that supports the `partial` class pattern, keeping the codebase organized and readable.
3.  **ALU and Flag Accuracy:** The implementation correctly models complex Z80 behaviors, including Decimal Adjust Accumulator (DAA) logic for both addition and subtraction, and the dual role of the Parity/Overflow (`P/V`) flag.
4.  **Memory Abstraction:** The `AddressDecoder` uses a "last-registration-wins" approach, which correctly models hardware where ROM shadowing or "phantom" RAM mirrors are common (e.g., the ZX80's partial address decoding).

## Critiques and Scaling Challenges

While the architecture is highly functional, it faces several challenges as it scales to more complex machines (e.g., the ZX Spectrum):

### 1. The `AddressDecoder` is an O(1) High-Speed Router (RESOLVED)
*   **The Issue:** The `AddressDecoder.Resolve()` method previously iterated through a `List<Mapping>` on every memory access.
*   **The Fix:** This has been refactored into a **Page Table** approach. The address space is divided into 256-byte pages, reducing routing to a constant-time O(1) array lookup. Page-alignment (256-byte boundaries) is enforced for stability.

### 2. Delegate Dispatch Overhead (IN PROGRESS)
The instruction dispatcher is transitioning from `Action` delegate arrays to a high-performance `switch` statement.
*   **The Issue:** Invoking a C# delegate incurs overhead (call-virt) compared to a flat `switch`.
*   **The Fix:** A code generator (`CpuZ80.CodeGen`) has been implemented. The "hot path" (e.g., `NOP`, `ADD A, r`) now executes via `StepGenerated()`, which JITs to a direct jump table. The remaining instruction set will be migrated incrementally.

### 3. Instruction-Level vs. T-State Level Timing
The CPU executes an entire instruction, performs memory operations instantly, and *then* adds the total elapsed cycles (e.g., `TotalCycles += 15UL;`).
*   **The Issue:** Real Z80 machines (like the ZX Spectrum) have "contended memory" where the ULA can pause the CPU mid-instruction on a specific T-State (clock cycle). The current design means memory timings are only accurate at the *end* of an instruction, not during it.
*   **The Fix:** To support complex video hardware, memory reads and writes need to report how many cycles they consumed, or the CPU needs to step cycle-by-cycle rather than instruction-by-instruction.

### 4. Branching Penalty for Indexed Modes
The `_indexMode` state machine avoids duplicating instruction logic for `IX` and `IY`.
*   **The Issue:** Because `GetReg` and `SetReg` contain `if (_indexMode != IndexMode.HL)`, every base instruction pays a minor branching penalty to check if it is currently prefixed.
*   **The Fix:** For maximum performance, standard instructions (which make up ~90% of executed code) should have a completely branch-free path, even if it means slightly duplicating the arithmetic logic for `IX`/`IY`.

### 5. Missing `MEMPTR` (WZ Register) Implementation
The code approximates undocumented flags (Bits 3 and 5, or X and Y) by taking them from the result or the Accumulator: `F = (byte)((F & ~0x28) | (A & 0x28))`.
*   **The Issue:** This is mostly correct, but the true Z80 derives these flags from a hidden 16-bit register (`WZ` or `MEMPTR`) during block instructions (`LDIR`, `CPIR`), `BIT` instructions, and `EX AF, AF'`. Without modeling `MEMPTR`, the emulation will fail the stricter undocumented ZEXALL tests (noted in the backlog as "minor CRC deviations").
*   **The Fix:** Implement the internal `MEMPTR` register and update the flag logic for block and bitwise operations to correctly derive bits 3 and 5 from it.

## Conclusion
The emulator is perfectly designed for machines like the CP/M or ZX80 (Epic 2). However, before building Epic 4 (the ZX Spectrum), refactoring the `AddressDecoder` for O(1) lookups and addressing the T-State timing issue will be necessary to correctly emulate the Spectrum's ULA memory contention and achieve professional-grade performance and accuracy.