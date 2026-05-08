# MEMPTR (WZ Register) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the internal `WZ` (MEMPTR) register to achieve 100% bit-perfect ZEXALL functional accuracy for undocumented flags.

**Architecture:** Add `ushort WZ` to the `Cpu` class. Update the `CpuZ80.CodeGen` to automate `WZ` updates for standard opcodes. Manually update complex block instructions and the `BIT n, (HL)` flag logic.

**Tech Stack:** C#, .NET 8, CpuZ80.CodeGen

---

### Task 1: Infrastructure - Add `WZ` and Flag Helper

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.cs`
- Modify: `src/CpuZ80.Core/Cpu.Arithmetic.cs`

- [ ] **Step 1: Add `WZ` field to `Cpu.cs`**

```csharp
public ushort WZ; // Internal temporary register (MEMPTR)
```
Add to the registers section and initialize to 0 in `Reset()`.

- [ ] **Step 2: Add `SetUndocumentedFlagsFromWZ` helper**

In `Cpu.Arithmetic.cs`:
```csharp
private void SetUndocumentedFlagsFromWZ()
{
    // Bits 3 and 5 of F are taken from bits 11 and 13 of WZ (high byte bits 3 and 5)
    F = (byte)((F & ~0x28) | ((WZ >> 8) & 0x28));
}
```

- [ ] **Step 3: Verify baseline tests pass**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter "FullyQualifiedName!~IntegrationTests"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/CpuZ80.Core/Cpu.cs src/CpuZ80.Core/Cpu.Arithmetic.cs
git commit -m "refactor: add WZ register and undocumented flag helper"
```

### Task 2: CodeGen Evolution - Automate `WZ` Updates

**Files:**
- Modify: `src/CpuZ80.CodeGen/Program.cs`

- [ ] **Step 1: Update `Instruction` record to include `WzAction`**

```csharp
public record Instruction(byte Opcode, string Mnemonic, string Action, int[] Cycles, string? WzAction = null);
```

- [ ] **Step 2: Update `GenerateCase` to inject `WzAction`**

In `Program.cs`:
```csharp
    static void GenerateCase(StringBuilder sb, Instruction inst) {
        sb.Append($"            case 0x{inst.Opcode:X2}: /* {inst.Mnemonic} */ ");
        // ... previous Tick logic ...
        if (!string.IsNullOrEmpty(inst.WzAction)) {
            sb.Append($"{inst.WzAction}; ");
        }
        sb.AppendLine("break;");
    }
```

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.CodeGen/Program.cs
git commit -m "feat: update generator to support WZ (MEMPTR) actions"
```

### Task 3: Instruction Migration - Implement `WZ` Rules

**Files:**
- Modify: `src/CpuZ80.CodeGen/Program.cs`
- Modify: `src/CpuZ80.Core/Cpu.Generated.cs` (via regeneration)

- [ ] **Step 1: Add `WZ` rules to common instructions in `Program.cs`**

Apply the following rules:
- `LD A, (nn)` (0x3A): `WZ = (ushort)(nn + 1)`
- `LD (nn), A` (0x32): `WZ = (ushort)((A << 8) | ((nn + 1) & 0xFF))`
- `LD A, (BC)` (0x0A): `WZ = (ushort)(BC + 1)`
- `LD A, (DE)` (0x1A): `WZ = (ushort)(DE + 1)`
- `LD HL, (nn)` (0x2A): `WZ = (ushort)(nn + 1)`
- `LD (nn), HL` (0x22): `WZ = (ushort)(nn + 1)`

- [ ] **Step 2: Implement `BIT n, (HL)` special case**

Update `BIT` instructions in `Program.cs` to use `SetUndocumentedFlagsFromWZ()` instead of result-based flags.

- [ ] **Step 3: Run generator and verify tests**

Run: `dotnet run --project src/CpuZ80.CodeGen -- src/CpuZ80.Core/Cpu.Generated.cs`
Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj`

- [ ] **Step 4: Commit**

```bash
git add src/CpuZ80.CodeGen/Program.cs src/CpuZ80.Core/Cpu.Generated.cs
git commit -m "feat: migrate standard instructions to MEMPTR model"
```

### Task 4: Block Operation Refactor

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.Extended.cs`

- [ ] **Step 1: Update `LDI`, `LDD`, `CPI`, `CPD` to maintain `WZ`**

Example for `LDI`:
```csharp
    private void LDI()
    {
        byte val = _bus.Read(HL++);
        _bus.Write(DE++, val);
        BC--;
        WZ = (ushort)(WZ + 1); // Simplified for this plan; follow documented WZ logic
        FlagN = false;
        FlagH = false;
        FlagPV = BC != 0;
        SetUndocumentedFlagsFromWZ();
    }
```

- [ ] **Step 2: Final verification with ZEXALL**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter IntegrationTests`
Expected: PASS with 100% CRC matches (no deviations).

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.Core/Cpu.Extended.cs
git commit -m "feat: implement MEMPTR logic for block operations"
```
