# Using the CpuZ80 emulator — walkthrough

This guide builds up from running a single instruction to inspecting Z80-specific state and register sets.

## Repository structure

```
CpuZ80/
  src/
    CpuZ80.Core/        — CPU, Ram, Rom, AddressDecoder, IBus, IPortBus
    Machines.Common/    — IVideoSink, IAudioSink, IPhysicalKeyboard, ITapeDevice, PhysicalKey
    Machines.Zx80/      — ZX80 machine compositor
    Adapters.Raylib/    — Raylib window: IVideoSink + IPhysicalKeyboard (copied from Cpu6502)
    Host.Zx80/          — ZX80 runnable entry point
  tests/
    CpuZ80.Tests/
    Machines.Zx80.Tests/
```

`Adapters.Raylib` and `Machines.Common` are copied from the sibling `Cpu6502` repo rather
than shared via a cross-repo reference. A future shared-adapters repo will replace the copies.

---

## 1. The bus interfaces

The Z80 uses two distinct address spaces: Memory and I/O.

```csharp
public interface IBus // Memory
{
    byte Read(ushort address);
    void Write(ushort address, byte value);
}

public interface IPortBus // I/O Ports
{
    byte In(ushort port);
    void Out(ushort port, byte value);
}
```

---

## 2. Running your first program

```csharp
var ram = new Ram(0x10000);   // 64 KB

// Write a program at $0100
ram.Load(0x0100, new byte[]
{
    0x3E, 0x05,   // LD A, $05   clear A
    0x06, 0x0A,   // LD B, $0A   loop counter = 10
    0x80,         // ADD A, B    A = A + B
    0x10, 0xFD,   // DJNZ -3     Decrement B, jump if not zero
    0x76          // HALT
});

var cpu = new Cpu(ram);
cpu.PC = 0x0100;

// Step until HALT
while (true)
{
    cpu.Step();
    // In this emulator, you'd check for a specific exit condition
    if (cpu.PC == 0x0106) break; 
}

Console.WriteLine(cpu.A);          // Result
Console.WriteLine(cpu.TotalCycles); 
```

---

## 3. Inspecting Z80 State

The Z80 has a rich set of registers, including alternate sets:

```csharp
cpu.A, cpu.F, cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L  // Main registers
cpu.BC, cpu.DE, cpu.HL                                  // 16-bit pairs
cpu.IX, cpu.IY                                          // Index registers
cpu.SP, cpu.PC                                          // Control registers

// Flags
cpu.FlagC // Carry
cpu.FlagZ // Zero
cpu.FlagS // Sign
cpu.FlagPV // Parity / Overflow
cpu.FlagN // Add/Sub
cpu.FlagH // Half-Carry

// Alternate registers are public for precision testing and CodeGen:
cpu.A_, cpu.F_, cpu.B_, cpu.C_, cpu.D_, cpu.E_, cpu.H_, cpu.L_

// Exchange opcodes manipulate them:
// 0x08: EX AF, AF'
// 0xD9: EXX
```

---

## 4. Building a machine

```csharp
var bus = new AddressDecoder(); // Use a decoder similar to 6502 if needed
bus.Map(0x0000, 0xFFFF, new Ram(0x10000));

var ports = new MyIOPorts(); // Implement IPortBus

var cpu = new Cpu(bus, ports);
```

---

## 5. Running at speed

The `TotalCycles` counter allows for precise timing:

```csharp
const ulong CyclesPerFrame = 4_000_000 / 50;   // 4 MHz, 50 Hz

ulong frameStart = cpu.TotalCycles;
while (cpu.TotalCycles - frameStart < CyclesPerFrame)
    cpu.Step();
```
