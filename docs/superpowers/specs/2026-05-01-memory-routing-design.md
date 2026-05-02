# Design Spec: O(1) Memory Routing (AddressDecoder Refactor)

**Date:** 2026-05-01
**Status:** Approved
**Topic:** Performance Optimization

## 1. Problem Statement
The current `AddressDecoder` uses a `List<Mapping>` and iterates through it on every memory access to resolve which device should handle a given 16-bit address. This is an $O(N)$ operation where $N$ is the number of mappings. As more devices (ROM, RAM, Peripherals) are added, the emulator's performance degrades linearly.

## 2. Proposed Architecture: Page Table
We will refactor the `AddressDecoder` to use a **Page Table** approach. By dividing the 64KB address space into 256-byte pages, we can route any address to its target device in constant time ($O(1)$).

### 2.1 Data Structures
We will define an internal `PageEntry` struct to store the device and its mapping offset:

```csharp
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
```

The decoder will maintain a fixed-size array of 256 entries:
`private readonly PageEntry[] _pages = new PageEntry[256];`

### 2.2 Routing Logic
Accessing memory will now be a simple array lookup:

- **Read:** `byte Read(ushort addr) => _pages[addr >> 8].Device?.Read((ushort)(addr - _pages[addr >> 8].BaseAddress)) ?? 0xFF;`
- **Write:** `void Write(ushort addr, byte val) => _pages[addr >> 8].Device?.Write((ushort)(addr - _pages[addr >> 8].BaseAddress), val);`

### 2.3 Mapping Logic
The `Map` method will populate the page table. This retains the "last-registration-wins" behavior of the current system.

```csharp
public void Map(ushort from, ushort to, IBus device)
{
    int startPage = from >> 8;
    int endPage = to >> 8;
    for (int i = startPage; i <= endPage; i++)
    {
        _pages[i] = new PageEntry(device, from);
    }
}
```

## 3. Constraints & Trade-offs
- **Granularity:** Hardware must be mapped in 256-byte chunks. Finer granularity (e.g., a 16-byte register block) will effectively "own" the entire 256-byte page it resides in.
- **Memory:** The 256-entry array consumes negligible memory (~2KB-4KB depending on architecture) compared to the performance gains.

## 4. Verification Plan
- **Unit Tests:** Update `AddressDecoderTests.cs` to ensure all existing routing behaviors (overlaps, open bus, offsets) remain identical.
- **Regression:** Run full system tests (`CpuZ80.Tests`) to ensure no breaking changes in CPU-to-memory communication.
