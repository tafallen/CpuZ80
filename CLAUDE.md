# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build everything
dotnet build

# Run all tests (includes coverage enforcement — fails if branch coverage < 85%)
dotnet test

# Run tests for a specific project
dotnet test tests/CpuZ80.Tests/

# Run a single test class
dotnet test --filter "ClassName=CpuZ80.Tests.ArithmeticTests"

# Run tests with coverage collection
dotnet test --collect:"XPlat Code Coverage"
```

## Repository layout

```
src/
  CpuZ80.Core/        — Z80 CPU, Ram, IBus, IPortBus
tests/
  CpuZ80.Tests/       — CPU unit tests + ZEXALL integration test
docs/
  walkthrough.md      — IBus / CPU tutorial
```

## Architecture

The CPU interacts with memory via `IBus` and with hardware ports via `IPortBus`.

### CPU implementation

`Cpu` is a `sealed partial` class split across files by instruction group:

| File | Content |
|---|---|
| `Cpu.cs` | Main registers, dispatch table building, and base opcodes |
| `Cpu.Arithmetic.cs` | 8-bit and 16-bit math, ALU logic, and flag helpers |
| `Cpu.ControlFlow.cs` | Jumps, Calls, Returns, and condition checking |
| `Cpu.Bitwise.cs` | CB prefix dispatcher (Shifts, Rotates, BIT/SET/RES) |
| `Cpu.Extended.cs` | ED prefix dispatcher (Block ops, 16-bit ADC/SBC, I/R registers) |
| `Cpu.Stack.cs` | Stack operations (PUSH/POP) |
| `Cpu.Indexed.cs` | DD/FD prefix dispatcher (IX/IY addressing and register halves) |

## Test pattern

All CPU tests inherit `CpuFixture`, which wires a 64 KB `Ram` as the bus.

```csharp
Load(0x0100, 0x3E, 0x42);  // LD A, $42
Step();
Assert.Equal(0x42, Cpu.A);
Assert.Equal(7UL, Cpu.TotalCycles);
```

Every test asserts **both** the observable state (registers/flags/memory) **and** the cycle count.
