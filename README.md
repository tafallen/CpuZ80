# CpuZ80

A high-performance, silicon-accurate Zilog Z80 CPU emulator in C#. 

## Key Features

- **100% Functional Accuracy**: Passes the exhaustive **ZEXALL** instruction exerciser with bit-perfect matches for all documented and undocumented behaviors (including Bits 3 and 5 of the F register).
- **Silicon-Accurate Timing**: Interleaves T-state cycles (clock ticks) with memory and I/O operations at the M-cycle level, enabling cycle-perfect hardware emulation.
- **High Performance**: Uses a code-generated `switch` dispatcher for 100% of instructions, leveraging JIT jump tables for maximum execution speed.
- **Composable Architecture**: The CPU is completely decoupled from hardware; it interacts with memory via `IBus` and I/O via `IPortBus`.
- **O(1) Memory Routing**: Includes an `AddressDecoder` with a page-table based router for constant-time memory access across complex hardware maps.

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

var cpu = new Cpu(ram);
cpu.PC = 0x0100;

// 3. Step until HALT
while (true)
{
    cpu.Step();
    if (cpu.PC == 0x0106) break; 
}

Console.WriteLine($"Result: {ram.Read(0x2000)}");   // → 2
Console.WriteLine($"Cycles: {cpu.TotalCycles}");
```

## Running the emulators

Each computer has its own host application in the `src/Host.*` directories.

### **Sinclair ZX80**
```bash
dotnet run --project src/Host.Zx80 -- --rom <path_to_zx80.rom> [options]
```
*   **Options**: `--tape <file.o>` to load a tape signal.

### **Sinclair ZX81**
```bash
dotnet run --project src/Host.Zx81 -- --rom <path_to_zx81.rom> [options]
```
*   **Options**: `--snapshot <file.p>` to instantly load a program.

### **Sinclair ZX Spectrum 48K**
```bash
dotnet run --project src/Host.ZxSpectrum -- --rom <path_to_48k.rom> [options]
```
*   **Options**: `--snapshot <file.sna>` to load a 48K snapshot.

**Common Options**:
*   `--scale <n>`: Window scale factor (default: 3).

---

## Key types

| Type | Purpose |
|---|---|
| `IBus` | Memory interface — implement this for RAM/ROM |
| `IPortBus` | I/O interface for hardware ports (`IN`/`OUT` instructions) |
| `Cpu` | The Z80 engine: `Step()`, `PC`, `SP`, and all registers (including alternate sets and internal `WZ`) |
| `AddressDecoder` | High-speed (O(1)) memory mapper for wiring machines |

## Engineering Standards

This project follows a strict **TDD (Test-Driven Development)** approach. All instructions are verified for both functional result and precise T-state cycle counts. The core engine is built using a custom Code Generator (`CpuZ80.CodeGen`) to ensure consistency and performance across all 1,000+ instruction variations.

## Validating Correctness

The core logic is verified using the standalone `CpuZ80.Exerciser` project, which executes the **ZEXALL** instruction suite. 

To run the exhaustive exerciser:
1. Ensure `tests/CpuZ80.Tests/TestData/zexall.bin` is present.
2. Run `dotnet run -c Release --project tests/CpuZ80.Exerciser`.
