# Z80 Emulator Architectural Analysis
**Date:** April 25, 2026
**Status:** In Progress (TDD Phase)

## 1. Core Design Philosophy
The Z80 emulator follows the same modular pattern as the Cpu6502 project, prioritizing **composition** and **cycle accuracy**.

### 1.1 Hardware Agnosticism
The CPU interacts with the outside world strictly through the `IBus` interface. This allows the core to be used in any Z80-based machine (e.g., ZX Spectrum, MSX, or ColecoVision) without modification.

### 1.2 Partial Class Structure
The `Cpu` class is marked as `partial`. As the instruction set grows, we will split instructions into separate files (e.g., `Cpu.Arithmetic.cs`, `Cpu.Bitwise.cs`) to maintain readability.

---

## 2. Component Breakdown

### 2.1 Register File
*   **Main Registers:** A, F, B, C, D, E, H, L (8-bit).
*   **Alternate Registers:** A', F', B', C', D', E', H', L' (accessible via `EX` and `EXX`).
*   **Index Registers:** IX, IY (16-bit).
*   **Control Registers:** PC (Program Counter), SP (Stack Pointer).
*   **Register Pairs:** BC, DE, HL are implemented as 16-bit properties mapped to the underlying 8-bit registers.

### 2.2 Instruction Dispatch
*   **Table-Driven:** A 256-slot `Action[]` array (`_ops`) handles the base instruction set.
*   **Prefix Handling (Planned):** Prefixes like `0xCB`, `0xED`, etc., will trigger a second dispatch into specialized tables.

---

## 3. Current Implementation State
*   [x] Basic Bus/RAM infrastructure.
*   [x] Primary and Alternate register files.
*   [x] NOP, LD r, n, LD r, r' instructions.
*   [x] Register Exchange instructions (EX AF, AF' and EXX).
*   [x] Status Flag Register (F) and flag logic.
*   [x] 8-bit Arithmetic Unit (ADD, ADC, SUB, SBC).
*   [x] 8-bit Logical Operations (AND, OR, XOR, CP).
*   [x] 8-bit Increment/Decrement (INC, DEC).
*   [x] 16-bit Immediate Loads (LD dd, nn).
*   [x] 16-bit Direct Memory Transfer (LD (nn), HL and LD HL, (nn)).
*   [x] 16-bit Arithmetic (ADD HL, ss).
*   [x] Stack Operations (PUSH, POP).
*   [x] Control Flow (JP, JR, CALL, RET - unconditional and conditional).
*   [x] Bitwise & Shifts (CB Prefix - 256 opcodes).
*   [x] Extended Instructions (ED Prefix - block ops, 16-bit ADC/SBC, system regs).
*   [ ] Index Registers (DD/FD Prefixes).
