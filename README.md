# CpuZ80

A high-performance, functionally complete Zilog Z80 CPU emulator in C#. Supports all base opcodes, all prefixes (CB, ED, DD, FD), automated block operations (LDIR, CPIR, etc.), and index register halves (IXH, IXL).

Designed for composing real 80s machine emulators — the CPU knows nothing about the machine it is in, it only talks to an `IBus` and an `IPortBus`.

## Quick start

```csharp
using CpuZ80.Core;

// 1. Create a 64 KB flat RAM bus
var ram = new Ram(0x10000);

// 2. Load a small program at $0100
ram.Load(0x0100, new byte[]
{
    0x3E, 0x01,   // LD A, $01
    0xC6, 0x01,   // ADD A, $01
    0x32, 0x00, 0x20, // LD ($2000), A
    0x76          // HALT
});

// 3. Create the CPU and run
var cpu = new Cpu(ram);
cpu.PC = 0x0100;

while (true)
{
    cpu.Step();
    if (ram.Read(0x2000) != 0) break;   // wait for result to appear
}

Console.WriteLine($"Result: {ram.Read(0x2000)}");   // → 2
Console.WriteLine($"Cycles: {cpu.TotalCycles}");
```

## Key types

| Type | Purpose |
|---|---|
| `IBus` | Interface the CPU talks to for memory — implement this for RAM/ROM |
| `IPortBus` | Interface for I/O ports (`IN`/`OUT` instructions) |
| `Ram` | Flat read/write memory with a `Load(address, bytes[])` helper |
| `Cpu` | The Z80 itself: `Step()`, `PC`, `SP`, and all registers (A, F, B, C, D, E, H, L, IX, IY, etc.) |

## Architecture

The emulator is designed for performance and maintainability through two core architectural features:

- **O(1) Memory Routing**: The `AddressDecoder` uses a 256-entry page table to route memory access in constant time, regardless of mapping complexity. Mappings are enforced on 256-byte page boundaries for stability and speed.
- **Hybrid Instruction Dispatch**:
    - **Fast Path (CodeGen)**: Performance-critical instructions (e.g., `NOP`, `ADD A, r`) are executed via a code-generated `switch` statement in `StepGenerated()`, eliminating delegate overhead.
    - **Tiered Dispatch Tables**: Less frequent or complex instructions use a modular system of `Action[]` arrays (`_ops`, `_cbOps`, `_edOps`, etc.).
    - **IndexMode**: A transient state pattern handles `IX` and `IY` prefixing without code duplication.

## Validating correctness

The core logic has been verified using the **ZEXALL** instruction exerciser. It successfully processes billions of cycles and passes all documented instruction logic, with minor CRC deviations only in undocumented flag bits (3 and 5).

To run the integration test:
1. Place `zexall.bin` in `tests/CpuZ80.Tests/TestData/`.
2. Run `dotnet test`.
