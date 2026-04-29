# CpuZ80 Backlog

## Goal

Emulate the Z80 CPU accurately enough to run 80s computer OS ROMs (CP/M, ZX Spectrum, etc.).
The CPU core is the first milestone. Hardware emulation follows.

---

## Z80 CPU — Remaining Work

### Interrupt Handling
- `INT` (maskable interrupt): modes 0, 1, and 2 — none are implemented
- `NMI` (non-maskable interrupt): jumps to 0x0066, pushes PC — not implemented
- IFF1/IFF2 are tracked but never acted on
- Most ROMs rely heavily on interrupts (keyboard, timers, display sync)

### Undocumented Opcodes
- `SLL` (CB 30–37) is implemented but classed as undocumented
- Several DD/FD prefix opcodes that fall through to the base table behave
  differently on real hardware (e.g. DD 00 is a NOP-like, not a true NOP)
- `IN F, (C)` (ED 70) — reads port, affects flags, discards result (currently
  stores to the `F` register index which is wrong)

### Accuracy Gaps
- `R` register increments on every M1 cycle; currently only incremented on
  `Fetch()` — prefixed instructions increment it twice but only count once
- Undocumented flags (bits 3 and 5 of F) are set for most instructions but
  not verified against the full ZEXALL undocumented suite

---

## Hardware Emulation (Future Milestones)

### Generic Infrastructure
- Memory-mapped I/O bus with configurable address decode
- ROM loading (read-only regions that ignore writes)
- Configurable memory banking / paging

### ZX Spectrum
- ULA chip: border colour, keyboard matrix, tape interface
- 48K memory map: 16K ROM + 48K RAM
- Display: 256×192 bitmap + attribute cells, 50Hz interrupt
- Beeper audio output

### CP/M Machines (e.g. Amstrad CPC, generic S-100)
- BIOS call shim at 0x0000–0x0005 (BDOS/BIOS entry points)
- Disk I/O abstraction (filesystem-backed virtual disk images)
- Console I/O via `IN`/`OUT` mapped to host terminal

### General
- Snapshot save/load (SNA / Z80 file formats for Spectrum)
- Cycle-accurate timing tied to a host clock source
- Debugger hooks (breakpoints, single-step, register watch)
