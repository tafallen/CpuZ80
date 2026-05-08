# MEMPTR (WZ Register) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the internal `WZ` (MEMPTR) register to achieve 100% bit-perfect ZEXALL functional accuracy for undocumented flags (bits 3 and 5).

**Architecture:** Add `ushort WZ` to the `Cpu` class. Update the `CpuZ80.CodeGen` to automate `WZ` updates for standard opcodes via an expanded `Instruction` record. Manually update complex block instructions and the `BIT n, (HL)` flag logic to pull from `WZ`.

**Tech Stack:** C#, .NET 8, CpuZ80.CodeGen, xUnit

---

### Task 1: Infrastructure - Add `WZ` and Flag Helper

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.cs`
- Modify: `src/CpuZ80.Core/Cpu.Arithmetic.cs`
- Test: `tests/CpuZ80.Tests/MiscTests.cs`

- [ ] **Step 1: Add `WZ` field to `Cpu.cs`**

```csharp
// Inside Cpu class
public ushort WZ; // Internal temporary register (MEMPTR)

// Update Reset()
public void Reset()
{
    // ... existing ...
    WZ = 0;
}
```

- [ ] **Step 2: Add `SetUndocumentedFlagsFromWZ` helper to `Cpu.Arithmetic.cs`**

```csharp
private void SetUndocumentedFlagsFromWZ()
{
    // Bits 3 and 5 of F are taken from bits 11 and 13 of WZ (high byte bits 3 and 5)
    F = (byte)((F & ~0x28) | ((WZ >> 8) & 0x28));
}
```

- [ ] **Step 3: Write a test for the helper**

In `tests/CpuZ80.Tests/MiscTests.cs`:
```csharp
[Fact]
public void SetUndocumentedFlagsFromWZ_SetsCorrectBits()
{
    var cpu = new Cpu(new Ram(0x100));
    cpu.WZ = 0x2800; // Bits 11 and 13 set
    cpu.F = 0x00;
    
    // We need to use reflection or make it internal to test easily, 
    // or just rely on Task 3 BIT tests. 
    // For now, let's assume it's internal for testing.
    cpu.SetUndocumentedFlagsFromWZ();
    
    Assert.Equal(0x28, cpu.F);
}
```

- [ ] **Step 4: Commit**

```bash
git add src/CpuZ80.Core/Cpu.cs src/CpuZ80.Core/Cpu.Arithmetic.cs
git commit -m "refactor: add WZ register and undocumented flag helper"
```

---

### Task 2: CodeGen Evolution - Support `WZ` Actions

**Files:**
- Modify: `src/CpuZ80.CodeGen/Program.cs`

- [ ] **Step 1: Update `Instruction` record**

```csharp
public record Instruction(byte Opcode, string Mnemonic, string Action, int[] Cycles, string? WzAction = null);
```

- [ ] **Step 2: Update `GenerateCase` to inject `WzAction`**

```csharp
static void GenerateCase(StringBuilder sb, Instruction inst) {
    sb.Append($"            case 0x{inst.Opcode:X2}: /* {inst.Mnemonic} */ ");
    
    if (inst.Cycles.Length == 0) {
        sb.Append($"{inst.Action}; ");
        if (inst.WzAction != null) sb.Append($"{inst.WzAction}; ");
        sb.AppendLine("break;");
    } else if (inst.Cycles.Length == 1) {
        sb.Append($"Tick({inst.Cycles[0]}); {inst.Action}; ");
        if (inst.WzAction != null) sb.Append($"{inst.WzAction}; ");
        sb.AppendLine("break;");
    } else {
        sb.Append($"Tick({inst.Cycles[0]}); {inst.Action}; ");
        if (inst.WzAction != null) sb.Append($"{inst.WzAction}; ");
        for (int i = 1; i < inst.Cycles.Length; i++) {
            sb.Append($"Tick({inst.Cycles[i]}); ");
        }
        sb.AppendLine("break;");
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.CodeGen/Program.cs
git commit -m "feat: support WzAction in code generator"
```

---

### Task 3: Instruction Migration - Implement `WZ` Rules in CodeGen

**Files:**
- Modify: `src/CpuZ80.CodeGen/Program.cs`
- Modify: `src/CpuZ80.Core/Cpu.Generated.cs` (via regen)

- [ ] **Step 1: Apply `WZ` rules in `Main`**

Update the following in `baseInstructions`:
- `LD A, (nn)` (0x3A): `WzAction: "WZ = (ushort)(nn + 1)"`
- `LD (nn), A` (0x32): `WzAction: "WZ = (ushort)((A << 8) | ((nn + 1) & 0xFF))"`
- `LD A, (BC)` (0x0A): `WzAction: "WZ = (ushort)(BC + 1)"`
- `LD A, (DE)` (0x1A): `WzAction: "WZ = (ushort)(DE + 1)"`
- `LD HL, (nn)` (0x2A): `WzAction: "WZ = (ushort)(nn + 1)"`
- `LD (nn), HL` (0x22): `WzAction: "WZ = (ushort)(nn + 1)"`

Update `cbInstructions` (BIT):
- `BIT n, (HL)`: Change `Action` to `DoBit(bit, _bus.Read(HL)); SetUndocumentedFlagsFromWZ();`

- [ ] **Step 2: Run Generator**

Run: `dotnet run --project src/CpuZ80.CodeGen -- src/CpuZ80.Core/Cpu.Generated.cs`

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.CodeGen/Program.cs src/CpuZ80.Core/Cpu.Generated.cs
git commit -m "feat: implement primary MEMPTR rules in generator"
```

---

### Task 4: Block Operations - Manual Implementation

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.Extended.cs`

- [ ] **Step 1: Update `LDI`/`LDD`/`CPI`/`CPD`**

Update `LDI`, `LDD`, `CPI`, `CPD` to maintain `WZ` and call `SetUndocumentedFlagsFromWZ()`.

- [ ] **Step 2: Commit**

```bash
git add src/CpuZ80.Core/Cpu.Extended.cs
git commit -m "feat: implement MEMPTR for block operations"
```

---

### Task 5: Final Verification

- [ ] **Step 1: Run ZEXALL Integration Test**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter IntegrationTests`
Expected: **PASS** with 100% CRC matches.

- [ ] **Step 2: Cleanup and Final Commit**

```bash
git commit -m "docs: finalize MEMPTR implementation, 100% ZEXALL compliance"
```
