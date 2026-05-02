# T-State Accuracy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the CPU timing model from "Instruction-at-a-time" to cycle-by-cycle "T-State" precision, enabling support for contended memory (ZX Spectrum).

**Architecture:** Implement a central `Tick(int count)` method in `Cpu`. Update the Code Generator to automatically interleave `Tick` calls with memory operations (M-cycles). Update legacy paths to call `Tick` instead of bulk cycle additions.

**Tech Stack:** C#, .NET 8, CpuZ80.CodeGen

---

### Task 1: Infrastructure - Add `Tick` Method

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.cs`
- Modify: `src/CpuZ80.Core/Cpu.Arithmetic.cs`
- Modify: `src/CpuZ80.Core/Cpu.Indexed.cs`
- Modify: `src/CpuZ80.Core/Cpu.Stack.cs`
- Modify: `src/CpuZ80.Core/Cpu.Extended.cs`

- [ ] **Step 1: Add `Tick` method and update helper methods**

Add to `src/CpuZ80.Core/Cpu.cs`:
```csharp
private void Tick(int count)
{
    TotalCycles += (ulong)count;
}
```

Replace all instances of `TotalCycles += NUL;` and `TotalCycles -= NUL;` with `Tick(N);` and `Tick(-N);` across all CPU partial classes.

- [ ] **Step 2: Verify tests still pass**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter "FullyQualifiedName!~IntegrationTests"`
Expected: PASS (161 tests)

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.Core/*.cs
git commit -m "refactor: add Tick(n) method and update legacy cycle counting"
```

### Task 2: CodeGen Evolution - Support M-Cycle Timing

**Files:**
- Modify: `src/CpuZ80.CodeGen/Program.cs`

- [ ] **Step 1: Update metadata model to support M-cycle arrays**

Update `src/CpuZ80.CodeGen/Program.cs`:
```csharp
public record Instruction(byte Opcode, string Mnemonic, string Action, int[] Cycles);
```

- [ ] **Step 2: Update generation logic to interleave `Tick` calls**

Modify the generation loop in `Program.cs` to produce code like:
```csharp
case 0x0A: /* LD A, (BC) */ Tick(4); A = _bus.Read(BC); Tick(3); break;
```

- [ ] **Step 3: Run generator (it will fail to compile until Task 3 update)**

Expected: CodeGen project compiles, but `Cpu.Generated.cs` will have syntax errors because metadata isn't updated yet.

- [ ] **Step 4: Commit**

```bash
git add src/CpuZ80.CodeGen/Program.cs
git commit -m "feat: update generator to support granular M-cycle timing"
```

### Task 3: Complete Migration to T-State Timing

**Files:**
- Modify: `src/CpuZ80.CodeGen/Program.cs`
- Modify: `src/CpuZ80.Core/Cpu.Generated.cs` (via regeneration)

- [ ] **Step 1: Update all instruction metadata in `Program.cs`**

Update all `new Instruction(...)` calls to use `int[]` for cycles.
Example:
- `4` -> `new[] { 4 }` (M1: Opcode Fetch)
- `7` -> `new[] { 4, 3 }` (M1: Fetch, M2: Memory Access)
- `10` -> `new[] { 4, 3, 3 }` (M1: Fetch, M2, M3)
- `11` -> `new[] { 4, 3, 4 }` (Wait-states for block ops)

- [ ] **Step 2: Run generator**

Run: `dotnet run --project src/CpuZ80.CodeGen -- src/CpuZ80.Core/Cpu.Generated.cs`

- [ ] **Step 3: Final verification**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj`
Expected: PASS (Including ZEXALL integration tests)

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: migrate all instructions to granular T-state timing"
```
