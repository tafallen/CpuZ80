# Design Spec: MEMPTR (WZ Register) Implementation

**Date:** 2026-05-03
**Status:** Approved
**Topic:** Functional Accuracy (Undocumented Flags)

## 1. Problem Statement
The current Z80 emulator approximates the undocumented flag bits (Bit 3: X and Bit 5: Y) by deriving them from the Accumulator or the result of an operation. While this is correct for many instructions, the true Z80 hardware derives these flags from a hidden 16-bit internal register called `MEMPTR` (or `WZ`) during specific instructions, such as block operations (`LDIR`, `CPIR`), `BIT` instructions, and `EX AF, AF'`. Without modeling `MEMPTR`, the emulator fails strict functional tests like **ZEXALL**.

## 2. Proposed Architecture: Generator-Managed MEMPTR
We will implement the internal `WZ` register and automate its updates using the `CpuZ80.CodeGen` framework. This ensures that every instruction maintains the correct internal state without requiring manual tracking across thousands of opcodes.

### 2.1 The `WZ` Register
A new 16-bit field will be added to the `Cpu` core:
```csharp
public ushort WZ; // Internal temporary register (MEMPTR)
```

### 2.2 Metadata-Driven Updates
The `Instruction` record in the code generator will be expanded to include `WzAction`.

```csharp
public record Instruction(
    byte Opcode, 
    string Mnemonic, 
    string Action, 
    int[] Cycles, 
    string? WzAction = null
);
```

**Example: `LD A, (nn)`**
- **Action:** `A = _bus.Read(nn)`
- **WzAction:** `WZ = (ushort)(nn + 1)`

### 2.3 Flag Logic Refactor
The undocumented flag logic will be updated to prioritize `WZ` bits 11 and 13 where appropriate.

```csharp
private void SetUndocumentedFlagsFromWZ()
{
    // Bits 3 and 5 of F are taken from bits 11 and 13 of WZ (high byte bits 3 and 5)
    F = (byte)((F & ~0x28) | ((WZ >> 8) & 0x28));
}
```

### 2.4 Handling "Tricky" Instructions
- **BIT n, (HL):** The generator will inject `WZ` updates during the memory fetch.
- **Block Instructions:** Logic in `Cpu.Extended.cs` for `LDI`, `LDD`, etc., will be manually updated to maintain `WZ`.
- **EX AF, AF':** `WZ` is unaffected, but the flags are set correctly using existing logic.

## 3. Constraints & Trade-offs
- **Complexity:** `MEMPTR` logic is famously obscure. We will follow documented behavioral patterns (e.g., from the "The Undocumented Z80 Documented" guide).
- **Performance:** Negligible overhead for updating a single 16-bit field.

## 4. Verification Plan
- **Unit Tests:** Update existing ALU and Load tests to verify `WZ` state where possible.
- **ZEXALL:** The primary success criteria is passing the undocumented flag CRC checks in the ZEXALL integration test.
- **Regression:** Ensure all existing documented functional tests still pass.
