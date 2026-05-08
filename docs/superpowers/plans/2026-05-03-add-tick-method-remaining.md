# Task 1: Infrastructure - Add Tick Method (Remaining Files) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace legacy cycle counting (`TotalCycles += N;`) with `Tick(N);` in the remaining CPU implementation files to support future T-state accurate emulation.

**Architecture:** Use the recently added `Tick(int count)` method in `Cpu.cs` which increments `TotalCycles` and provides a hook for future contention/delay logic.

**Tech Stack:** C#, .NET 8

---

### Task 1: Update Cpu.Extended.cs

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.Extended.cs`

- [ ] **Step 1: Replace TotalCycles += NUL; and TotalCycles -= NUL; with Tick(N); and Tick(-N);**
  Run PowerShell command to replace matches.

### Task 2: Update Cpu.Bitwise.cs

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.Bitwise.cs`

- [ ] **Step 1: Replace TotalCycles += NUL; and TotalCycles -= NUL; with Tick(N); and Tick(-N);**
  Run PowerShell command to replace matches.

### Task 3: Update Cpu.ControlFlow.cs

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.ControlFlow.cs`

- [ ] **Step 1: Replace TotalCycles += NUL; and TotalCycles -= NUL; with Tick(N); and Tick(-N);**
  Run PowerShell command to replace matches.

### Task 4: Update Cpu.Generated.cs

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.Generated.cs`

- [ ] **Step 1: Replace TotalCycles += NUL; and TotalCycles -= NUL; with Tick(N); and Tick(-N);**
  Run PowerShell command to replace matches.

### Task 5: Update Cpu.Interrupts.cs

**Files:**
- Modify: `src/CpuZ80.Core/Cpu.Interrupts.cs`

- [ ] **Step 1: Replace TotalCycles += NUL; and TotalCycles -= NUL; with Tick(N); and Tick(-N);**
  Run PowerShell command to replace matches.

### Task 6: Verification

- [ ] **Step 1: Run unit tests**
  Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter "FullyQualifiedName!~IntegrationTests"`
  Expected: 161 tests PASS

- [ ] **Step 2: Commit changes**
  Run: `git add src/CpuZ80.Core/*.cs && git commit -m "refactor: add Tick(n) method and update legacy cycle counting"`
