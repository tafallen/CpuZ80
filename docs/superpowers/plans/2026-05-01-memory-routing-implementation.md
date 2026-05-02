# O(1) Memory Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `AddressDecoder` from O(N) list-based routing to an O(1) 256-entry page table for professional-grade performance.

**Architecture:** Use a 256-entry array of `PageEntry` structs, where each page represents 256 bytes. Routing uses bit-shifting (`address >> 8`) for constant-time lookups.

**Tech Stack:** C#, .NET, XUnit

---

### Task 1: Initialize Page Table Data Structure

**Files:**
- Modify: `src/CpuZ80.Core/AddressDecoder.cs`

- [ ] **Step 1: Verify existing tests pass**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter AddressDecoderTests`
Expected: PASS

- [ ] **Step 2: Add PageEntry struct and _pages array**

```csharp
namespace CpuZ80.Core;

public sealed class AddressDecoder : IBus
{
    private readonly struct PageEntry
    {
        public readonly IBus? Device;
        public readonly ushort BaseAddress;

        public PageEntry(IBus? device, ushort baseAddress)
        {
            Device = device;
            BaseAddress = baseAddress;
        }
    }

    private readonly PageEntry[] _pages = new PageEntry[256];
    private readonly List<Mapping> _mappings = new(); // Keep for now to avoid compilation errors
    // ...
}
```

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.Core/AddressDecoder.cs
git commit -m "refactor: add PageEntry struct and internal page table array"
```

### Task 2: Implement Page-Based Mapping

**Files:**
- Modify: `src/CpuZ80.Core/AddressDecoder.cs`

- [ ] **Step 1: Update Map method to populate _pages**

```csharp
    public void Map(ushort from, ushort to, IBus device)
    {
        _mappings.Add(new Mapping(from, to, device)); // Keep for now

        int startPage = from >> 8;
        int endPage = to >> 8;

        for (int i = startPage; i <= endPage; i++)
        {
            _pages[i] = new PageEntry(device, from);
        }
    }
```

- [ ] **Step 2: Run tests to ensure no regressions**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter AddressDecoderTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/CpuZ80.Core/AddressDecoder.cs
git commit -m "refactor: update Map to populate page table"
```

### Task 3: Refactor Read/Write to O(1)

**Files:**
- Modify: `src/CpuZ80.Core/AddressDecoder.cs`

- [ ] **Step 1: Update Read and Write to use _pages**

```csharp
    public byte Read(ushort address)
    {
        var entry = _pages[address >> 8];
        return entry.Device is not null ? entry.Device.Read((ushort)(address - entry.BaseAddress)) : (byte)0xFF;
    }

    public void Write(ushort address, byte value)
    {
        var entry = _pages[address >> 8];
        entry.Device?.Write((ushort)(address - entry.BaseAddress), value);
    }
```

- [ ] **Step 2: Remove old Mapping logic**

Remove `Mapping` record struct, `_mappings` list, and the `Resolve` method.

- [ ] **Step 3: Final verification of tests**

Run: `dotnet test tests/CpuZ80.Tests/CpuZ80.Tests.csproj --filter AddressDecoderTests`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/CpuZ80.Core/AddressDecoder.cs
git commit -m "refactor: switch Read/Write to O(1) and remove old Resolve logic"
```
