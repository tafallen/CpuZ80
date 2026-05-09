# Z80 Emulator Architectural Critique and Improvement Opportunities

## Strengths and Elegant Design Choices

1.  **Generated Switch Dispatcher:** The core architecture uses a high-performance, generated `switch` dispatcher (`Cpu.Generated.cs`). This eliminates delegate overhead and allows the JIT to produce optimal jump tables, significantly outperforming the previous `Action[]` delegate approach.
2.  **Modular CPU Design:** Functional logic is cleanly separated into partial classes (`Cpu.Arithmetic.cs`, `Cpu.Bitwise.cs`, etc.), while the dispatcher is handled by the CodeGen engine. This provides a maintainable balance between manual logic and automated boiler-plate.
3.  **ALU and Flag Accuracy:** The implementation correctly models complex Z80 behaviors, including Decimal Adjust Accumulator (DAA) logic and the dual role of the Parity/Overflow (`P/V`) flag.
4.  **Memory Abstraction:** The `AddressDecoder` uses a "last-registration-wins" approach with an O(1) high-speed router, correctly modeling ROM shadowing and RAM mirrors.

## Scaling Challenges (Now RESOLVED)

The initial architecture faced several scaling challenges which have been successfully addressed:

### 1. High-Speed Memory Routing (RESOLVED)
*   **The Issue:** The `AddressDecoder.Resolve()` method previously iterated through a `List<Mapping>` on every memory access.
*   **The Fix:** Refactored into a **Page Table** approach. The address space is divided into 256-byte pages, reducing routing to a constant-time O(1) array lookup.

### 2. Delegate Dispatch Overhead (RESOLVED)
*   **The Issue:** Invoking C# delegates for every instruction incurred significant overhead.
*   **The Fix:** Implemented a full **Code Generator** (`CpuZ80.CodeGen`) that produces a unified `switch` dispatcher. 100% of instructions are now dispatched via this mechanism.

### 3. T-State Granularity and Contention (RESOLVED)
*   **The Issue:** Timings were previously updated only at the end of an instruction.
*   **The Fix:** Migrated to a **Granular T-State Model**. `Tick(n)` calls are now interleaved with memory reads, writes, and opcode fetches at the M-cycle level. This enables accurate emulation of ZX Spectrum ULA memory contention.

### 4. Prefix Redirection and Branching (RESOLVED)
*   **The Issue:** Handling `IX`/`IY` prefixes via a transient state machine introduced branching penalties and maintenance complexity.
*   **The Fix:** Switched to **Explicit Prefix Tables**. The CodeGen engine generates dedicated dispatch tables for `DD` and `FD` prefixes, ensuring zero branching penalty for base instructions and silicon-accurate transformation rules for indexed opcodes.

### 5. Bit-Perfect Flag Accuracy via MEMPTR (RESOLVED)
*   **The Issue:** Undocumented flags (Bits 3 and 5) were previously approximated.
*   **The Fix:** Implemented the hidden 16-bit **MEMPTR (WZ)** internal register. All flag-affecting instructions (including Block Ops and `BIT`) now derive Bit 3 and 5 from `WZ` where appropriate, achieving 100% pass rates in ZEXALL.

## Conclusion
The **CpuZ80** core is now a high-performance, professional-grade emulation engine. By resolving the bottlenecks in memory routing, instruction dispatch, and timing granularity, the architecture is fully prepared for cycle-accurate hardware emulation of complex Z80-based machines like the ZX Spectrum.
