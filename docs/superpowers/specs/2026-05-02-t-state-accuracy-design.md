# Design Spec: T-State Accuracy (Cycle-by-Cycle Timing)

**Date:** 2026-05-02
**Status:** Approved
**Topic:** Timing Accuracy & Contended Memory

## 1. Problem Statement
The current emulator uses "Atomic Timing," where the total cycles for an instruction are added in a single block (usually at the end of the instruction or in a logic helper). While functional for simple machines, this prevents accurate emulation of hardware like the ZX Spectrum, where the video hardware (ULA) can pause the CPU at specific clock cycles (T-States) during an instruction's execution.

## 2. Proposed Architecture: Granular T-State Ticking
We will refactor the timing model to use a cycle-by-cycle "Tick" mechanism. This moves the temporal resolution of the emulator from the "Instruction level" to the "T-State level."

### 2.1 The `Tick` Method
A new `Tick(int count)` method will be added to the `Cpu` class. Initially, it will simply increment `TotalCycles`, but it provides a central injection point for future memory contention logic.

```csharp
private void Tick(int count)
{
    TotalCycles += (ulong)count;
    // Future: _bus.Sync(count); or Contention check
}
```

### 2.2 Generator-Managed M-Cycles
The `CpuZ80.CodeGen` will be updated to handle granular timing. Most Z80 instructions follow a pattern of Machine Cycles (M-Cycles). 

**Metadata Change:**
Instructions will define an array of cycle counts corresponding to their M-cycles.

```csharp
new Instruction(0x0A, "LD A, (BC)", "A = _bus.Read(BC)", cycles: new int[] { 4, 3 })
```

**Generated Code:**
The generator will interleave `Tick` calls with logic:

```csharp
case 0x0A: /* LD A, (BC) */ 
    Tick(4);           // M1: Opcode Fetch
    A = _bus.Read(BC); // Execution
    Tick(3);           // M2: Memory Read
    break;
```

### 2.3 Legacy Path Transition
Helper methods and instructions not yet in the generator will be updated to call `Tick(N)` instead of `TotalCycles += N`. This ensures timing consistency across the entire core during the migration.

## 3. Constraints & Trade-offs
- **Complexity:** The code generator becomes more complex as it must handle different cycle distributions for over 1,000 opcodes.
- **Performance:** Slight overhead for multiple `Tick` calls vs one atomic addition. However, this is negligible compared to the accuracy gains and the efficiency of the `switch` dispatcher.

## 4. Verification Plan
- **Unit Tests:** Existing tests already assert `TotalCycles` at the end of `Step()`. These tests MUST continue to pass with identical values.
- **Cycle-Count Regression:** A temporary "Audit" mode could be added to `Tick` to ensure it isn't called with negative values or skipped.
- **ZEXALL:** Must still pass all documented instruction groups.
