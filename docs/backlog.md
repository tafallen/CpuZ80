# CpuZ80 Backlog

## Goal

Emulate the Z80 CPU accurately enough to run 80s computer OS ROMs (CP/M, ZX Spectrum, etc.).
The CPU core is the first milestone. Hardware emulation follows.

---

## Z80 CPU — Remaining Work

### Interrupt Handling
- ~~`INT` (maskable interrupt): modes 0, 1, and 2 — none are implemented~~ ✓
- ~~`NMI` (non-maskable interrupt): jumps to 0x0066, pushes PC — not implemented~~ ✓
- ~~IFF1/IFF2 are tracked but never acted on~~ ✓

### Undocumented Opcodes
- ~~`SLL` (CB 30–37) is implemented but classed as undocumented~~ ✓ (correctly implemented)
- ~~Several DD/FD prefix opcodes that fall through to the base table~~ ✓ (resolved via `_indexMode` mechanism)
- ~~`IN F, (C)` (ED 70) — stores to wrong register~~ ✓ (value discarded correctly, flags set correctly)

### Accuracy Gaps
- ~~`R` register double-increment for prefixed instructions~~ ✓ (`Fetch()` is called once per byte fetched, which is correct Z80 behaviour)
- ~~Undocumented flags (bits 3 and 5 of F) are set for most instructions but
  not verified against the full ZEXALL undocumented suite~~ ✓
- ~~`EI` one-instruction interrupt delay — INT was accepted immediately after EI~~ ✓
- ~~Invalid `ED` opcodes threw exceptions instead of acting as NOPs~~ ✓
- ~~`R` register did not increment during HALT~~ ✓

---

## Hardware Emulation (Future Milestones)

---

### Epic 1 — Composable Infrastructure

Mirrors the pattern established in the sibling Cpu6502 project so that machines are built
from reusable, independently testable components.

~~**US-101 — Rom class**
As a machine builder, I want a `Rom` class in `CpuZ80.Core` that implements `IBus`, accepts
a `byte[]` on construction, returns bytes on read, and silently ignores writes — so that ROM
regions behave correctly when mapped into an `AddressDecoder`.
- Acceptance: unit tests confirm reads return loaded data; writes are a no-op with no exception.~~ ✓

~~**US-102 — Ram improvements**
As a machine builder, I want `CpuZ80.Core.Ram` to expose a `RawBytes` property and a
`Load(ushort baseAddress, byte[] data)` helper — matching the Cpu6502 `Ram` API — so that
chips sharing the bus (e.g. a video generator reading display RAM) can access the backing
buffer directly, and ROM images can be bulk-loaded without byte-by-byte writes.
- Acceptance: existing tests still pass; new tests cover `Load()` and `RawBytes` round-trip.~~ ✓

~~**US-103 — AddressDecoder**
As a machine builder, I want an `AddressDecoder` class in `CpuZ80.Core` that implements
`IBus`, accepts `Map(ushort from, ushort to, IBus device)` registrations, routes reads/writes
to the correct device by address range, returns `0xFF` for unmapped reads, and applies
last-registration-wins on overlapping ranges — so that I can wire a full memory map from
discrete components without custom bus logic in each machine.
- Acceptance: tests cover single mapping, multiple non-overlapping mappings, overlap
  (last wins), unmapped read returns `0xFF`, unmapped write is silent, device sees
  zero-based offset (address minus region base).~~ ✓

~~**US-104 — Machines.Common project**
As a machine builder, I want a `Machines.Common` project (namespace `Machines.Common`)
containing the shared host-integration interfaces — `IVideoSink`, `IAudioSink`,
`IPhysicalKeyboard`, `ITapeDevice`, and the `PhysicalKey` enum — matching the identical
interfaces in the Cpu6502 sibling project so that host adapters (Raylib, WPF, etc.) can
target both emulators without duplication.
- Acceptance: project compiles; interfaces are identical in signature to `Cpu6502/src/Machines.Common`.~~ ✓

---

### Epic 2 — ZX80 Machine

#### Implementation approach: discrete logic

The ZX80 has no programmable chips — its "hardware" is a small number of TTL logic
gates. Rather than modelling each gate as an `IBus` class (which would be over-engineering
for single-gate behaviours), all display and sync logic lives directly inside
`Zx80Machine.RunFrame()` and `RenderFrame()`. This matches the hardware reality and keeps
the machine class self-contained. The specific gate behaviours to implement inline are:

- **NOP mapper** (single NOT gate on data bus bit 6): during display scan, reads from
  display RAM have bit 6 inverted so character codes become `0x00` (NOP). Implemented
  as a conditional transform inside the bus read path during scan cycles.
- **Sync generation** (NOR gate on `/HALT`): when the CPU halts at end of a display line,
  `/HSYNC` is asserted. `/VSYNC` is a longer halt at frame end. Implemented as HALT
  detection in `RunFrame()`.
- **Character generation**: during display fetch cycles the high byte of the PC drives the
  character ROM row, producing dot patterns. Implemented in `RenderFrame()` by reading
  character data from the ROM's `RawBytes` via the display file in RAM.
- **Keyboard**: the matrix is read via `IN` on the lower address bus — no chip, just
  pull-downs. Implemented in `IPortBus.In()` inside the machine.

Display RAM (`Ram.RawBytes`) is shared between the CPU bus and `RenderFrame()`, exactly
as the Acorn Atom's VideoRam is shared with the Mc6847 in the Cpu6502 sibling.

**US-201 — Machines.Zx80 project skeleton**
As a developer, I want a `Machines.Zx80` project with a `Zx80Machine` class that wires
together: `AddressDecoder` bus, 4K `Rom` at `0x0000–0x0FFF`, 1K `Ram` at `0x4000–0x43FF`,
and the `Cpu` — so that the machine can be constructed from a ROM image and stepped
instruction by instruction.
- Constructor: `Zx80Machine(byte[] rom, IPhysicalKeyboard? keyboard = null, ITapeDevice? tape = null)`
- Public surface: `Cpu`, `Bus`, `Rom`, `Ram`, `Reset()`, `Step()`, `RunFrame()`
- Acceptance: `Machines.Zx80.Tests` project; test constructs machine with a stub ROM,
  calls `Reset()`, and asserts `Cpu.PC` is read from the reset vector.

**US-202 — ZX80 keyboard matrix**
As a user, I want the ZX80 keyboard (8 half-rows × 5 keys) decoded from `IPhysicalKeyboard`
and returned via `IN` reads on the lower address bus — so that key presses are visible to
the ROM BASIC interpreter.
- Implementation: `IPortBus` inner class inside `Zx80Machine`; address lines A8–A15
  select the half-row, result byte has bits 0–4 low for pressed keys (active low).
- Acceptance: tests drive `IPhysicalKeyboard` stubs and assert the correct `IN` result byte
  for each half-row.

**US-203 — ZX80 display (software vsync)**
As a user, I want `RenderFrame()` to accept an `IVideoSink` and produce the ZX80's
software-generated 256×192 display — the CPU writes character data to RAM and the display
is built by scanning display RAM during `RunFrame()` — so that the screen updates at ~50 Hz.
- Implementation: `RunFrame()` detects HALT (end-of-line sync) and counts scan lines;
  `RenderFrame()` walks the display file in RAM, reads 8-pixel dot rows from the ROM
  character table, and submits an ARGB32 frame to `IVideoSink`. NOP-mapper logic
  (bit 6 inversion) applied inline during display scan. No separate chip class.
- Acceptance: integration test with a minimal ROM that writes a known character to display
  RAM; `RenderFrame()` produces the expected pixel pattern.

**US-204 — ZX80 tape**
As a user, I want `ITapeDevice` wired to the ROM's load/save routines via the relevant
memory-mapped I/O so that `.p` / `.o` tape images can be loaded into the emulator.
- Acceptance: test loads a known tape image and asserts that RAM contains the expected bytes
  after the ROM load routine completes.

---

### Epic 3 — ZX81 Machine

**US-301 — Machines.Zx81 project skeleton**
As a developer, I want a `Machines.Zx81` project with a `Zx81Machine` class wiring: 8K
`Rom` at `0x0000–0x1FFF`, 1K `Ram` at `0x4000–0x43FF` (expandable), `AddressDecoder` bus,
and `Cpu` — so that the machine can be constructed, reset, and stepped.
- Constructor: `Zx81Machine(byte[] rom, IPhysicalKeyboard? keyboard = null, ITapeDevice? tape = null)`
- Public surface mirrors `Zx80Machine`.
- Acceptance: `Machines.Zx81.Tests`; construct with stub ROM, `Reset()`, assert PC from
  reset vector.

**US-302 — ZX81 NMI-driven display (SLOW mode)**
As a user, I want `RunFrame()` to fire NMI at the start of each display line when in SLOW
mode — causing the CPU to HALT and the NMI handler to generate the next line of video —
so that the ZX81 display loop works as on real hardware.
- Acceptance: test confirms NMI fires at the correct cycle count intervals and the CPU
  HALTs between NMIs for the expected number of cycles.

**US-303 — ZX81 FAST mode**
As a user, I want the emulator to detect when the ZX81 is in FAST mode (NMI generator
disabled) and run `RunFrame()` without injecting NMIs — so that FAST mode programs run at
full speed without display output.
- Acceptance: test switches to FAST mode and asserts no NMI is delivered for a full frame.

**US-304 — ZX81 tape**
As a user, I want `.p` tape images loadable via `ITapeDevice` using the ZX81 ROM's tape
routines — so that programs can be loaded from virtual tape.
- Acceptance: mirrors US-204.

---

### Epic 4 — ZX Spectrum

**US-401 — Machines.ZxSpectrum project skeleton**
As a developer, I want a `Machines.ZxSpectrum` project with a `ZxSpectrumMachine` class
wiring: 16K `Rom` at `0x0000–0x3FFF`, 48K `Ram` at `0x4000–0xFFFF`, and `Cpu`.
- Constructor: `ZxSpectrumMachine(byte[] rom, IPhysicalKeyboard? keyboard = null, IAudioSink? audio = null, ITapeDevice? tape = null)`
- Acceptance: construct with stub ROM, `Reset()`, assert PC from reset vector.

**US-402 — ULA: keyboard and border**
As a user, I want the ULA's `IN 0xFE` keyboard half-row reads and `OUT 0xFE` border/speaker
writes implemented — so that the ROM can scan the keyboard and produce border colour and
beeper output.
- Acceptance: tests drive stub keyboard and assert `IN` results; `OUT` border writes are
  captured and exposed on the machine.

**US-403 — ULA: 50Hz INT**
As a user, I want the ULA to assert the Z80 INT line once per frame (~69,888 T-states at
3.5 MHz) — so that the ROM's interrupt-driven keyboard scan and display flash routine runs
correctly.
- Acceptance: test runs 70,000 cycles and asserts exactly one INT was accepted.

**US-404 — ULA: display rendering**
As a user, I want `RenderFrame()` to accept an `IVideoSink` and produce a 256×192 pixel
image from the Spectrum's bitmap + attribute RAM — so that the screen updates at 50 Hz.
- Acceptance: test writes known bitmap and attribute bytes, calls `RenderFrame()`, and
  asserts the expected ARGB pixel values.

**US-405 — Beeper audio**
As a user, I want `OUT 0xFE` bit 4 (speaker) changes captured and submitted to `IAudioSink`
as signed-16-bit mono samples at 44100 Hz — so that beeper music and sound effects are
audible on the host.
- Acceptance: test toggles the speaker bit at a known frequency and asserts the submitted
  samples contain the expected square wave.

**US-406 — Tape: .tap file loading**
As a user, I want `.tap` file blocks loadable via `ITapeDevice` using the Spectrum ROM
loader — so that commercial software tape images work.
- Acceptance: test loads a minimal `.tap` file and asserts RAM contains the expected block.

---

### Epic 5 — CP/M

**US-501 — CP/M machine skeleton**
As a developer, I want a `Machines.Cpm` project with a `CpmMachine` that loads a flat
binary into RAM at `0x0100` and provides a minimal BDOS shim at `0x0000` and `0x0005` —
so that CP/M `.com` programs can run under the emulator.
- Constructor: `CpmMachine(byte[] program)`
- Acceptance: load a minimal "Hello World" `.com` binary; run until PC hits `0x0000`;
  assert the expected string was output via the BDOS `C_WRITESTR` call.

**US-502 — Console I/O**
As a user, I want BDOS function 2 (C_WRITE) and function 9 (C_WRITESTR) mapped to host
`stdout` — so that CP/M programs that print to the console work from the command line.
- Acceptance: integration test captures stdout and asserts the correct output.

**US-503 — Disk I/O abstraction**
As a user, I want BDOS disk functions (open, read, write sector) backed by a virtual disk
image (e.g. `.img` file) — so that CP/M programs that access the filesystem work.
- Acceptance: test mounts a minimal disk image, runs a program that reads a file, and
  asserts the correct data is returned.

---

### General

- Snapshot save/load (SNA / Z80 file formats for Spectrum)
- Cycle-accurate timing tied to a host clock source
- Debugger hooks (breakpoints, single-step, register watch)
