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

~~**US-201 — Machines.Zx80 project skeleton**
As a developer, I want a `Machines.Zx80` project with a `Zx80Machine` class that wires
together: `AddressDecoder` bus, 4K `Rom` at `0x0000–0x0FFF`, 1K `Ram` at `0x4000–0x43FF`,
and the `Cpu` — so that the machine can be constructed from a ROM image and stepped
instruction by instruction.
- Constructor: `Zx80Machine(byte[] rom, IPhysicalKeyboard? keyboard = null, ITapeDevice? tape = null)`
- Public surface: `Cpu`, `Bus`, `Rom`, `Ram`, `Reset()`, `Step()`, `RunFrame()`
- Acceptance: `Machines.Zx80.Tests` project; test constructs machine with a stub ROM,
  calls `Reset()`, and asserts `Cpu.PC` is read from the reset vector.~~ ✓

**Implementation plan — US-201**

_New files:_
- `src/Machines.Zx80/Machines.Zx80.csproj` — references `CpuZ80.Core` and `Machines.Common`
- `src/Machines.Zx80/Zx80Machine.cs` — machine compositor
- `tests/Machines.Zx80.Tests/Machines.Zx80.Tests.csproj` — references `Machines.Zx80` and xUnit
- `tests/Machines.Zx80.Tests/Zx80MachineTests.cs` — machine tests

_Memory map wired in constructor:_
```
0x0000–0x0FFF  Rom  (4K — BASIC/OS ROM image)
0x4000–0x43FF  Ram  (1K — system variables + display file + BASIC program)
0x4400–0xFFFF  unmapped → 0xFF
```
Note: the ZX80 uses A14-based partial address decoding so the ROM also appears at 0x2000,
0x8000, and 0xA000, and RAM mirrors throughout 0x4000–0x7FFF and again at 0xC000–0xFFFF.
The `AddressDecoder` maps only the primary ranges above; mirrors are not needed for correct
ROM execution but are noted for accuracy if issues arise.

_`Reset()`:_ sets `Cpu.PC = 0x0000`, clears `Cpu.IFF1`/`IFF2`, sets `Cpu.SP = 0xFFFF`,
sets `Cpu.I = 0x0E` (the ZX80 ROM requires I=0x0E so the character generator reads font
data from the correct ROM offset at 0x0E00).
The Z80 has no memory-mapped reset vector — it simply starts execution at address 0.

_`Step()`:_ delegates to `Cpu.Step()`.

_`RunFrame()`:_ steps the CPU for one frame's worth of T-states
(3,250,000 Hz ÷ 50 Hz = **64,167 T-states**). Halted cycles count toward the frame budget.

_Test cases:_
1. `Reset_SetsPCToZero` — construct with a minimal 4K ROM stub (all NOPs), call `Reset()`,
   assert `Cpu.PC == 0x0000`.
2. `Reset_DisablesInterrupts` — assert `Cpu.IFF1 == false` and `Cpu.IFF2 == false` after reset.
2a. `Reset_SetsIRegisterTo0x0E` — assert `Cpu.I == 0x0E` after reset.
3. `Step_ExecutesOneInstruction` — load a NOP at 0x0000, call `Step()`, assert `Cpu.PC == 0x0001`.
4. `RunFrame_AdvancesCyclesByOneFrame` — call `RunFrame()`, assert `Cpu.TotalCycles >= 64167`.

---

**US-202 — ZX80 keyboard matrix**
As a user, I want the ZX80 keyboard (8 half-rows × 5 keys) decoded from `IPhysicalKeyboard`
and returned via `IN` reads on the lower address bus — so that key presses are visible to
the ROM BASIC interpreter.
- Implementation: `IPortBus` inner class inside `Zx80Machine`; address lines A8–A15
  select the half-row, result byte has bits 0–4 low for pressed keys (active low).
- Acceptance: tests drive `IPhysicalKeyboard` stubs and assert the correct `IN` result byte
  for each half-row.

**Implementation plan — US-202**

_ZX80 keyboard matrix — half-row to address line and key mapping:_

| A line low | Port high byte | Bit 0 | Bit 1 | Bit 2 | Bit 3 | Bit 4 |
|---|---|---|---|---|---|---|
| A8  | 0xFE | Shift | Z | X | C | V |
| A9  | 0xFD | A | S | D | F | G |
| A10 | 0xFB | Q | W | E | R | T |
| A11 | 0xF7 | 1 | 2 | 3 | 4 | 5 |
| A12 | 0xEF | 0 | 9 | 8 | 7 | 6 |
| A13 | 0xDF | P | O | I | U | Y |
| A14 | 0xBF | NEWLINE | L | K | J | H |
| A15 | 0x7F | Space | Period | M | N | B |

Notes:
- Bit 0 is the outermost key in each row (SHIFT side on left rows, SPACE/0 side on right rows).
- The ZX80 has **no Symbol Shift key** — that is a ZX Spectrum addition. Bit 1 of the A15
  row is the **Period (.)** key.
- The return key is physically labelled **NEWLINE** on the ZX80 keyboard.
- A pressed key pulls its result bit **low** (active low); unpressed = 1. Bits 5–7 always 1.

The full 16-bit port address is passed to `IPortBus.In(ushort port)`. The high byte
selects the half-row(s) — a 0 bit in the high byte activates that row. Multiple rows may
be selected simultaneously (for compound key detection).

_PC keyboard mapping (`PhysicalKey` enum → ZX80 key):_

| ZX80 key | `PhysicalKey` value |
|---|---|
| SHIFT | `LeftShift` |
| A–Z | `A`–`Z` |
| 0–9 | `D0`–`D9` |
| NEWLINE | `Return` |
| SPACE | `Space` |
| Period | `Period` |

No ZX80 keys lack a direct PC equivalent — the keyboard is purely alphanumeric plus SHIFT,
NEWLINE, SPACE, and Period.

_New files:_
- `src/Machines.Zx80/Zx80KeyboardAdapter.cs` — maps `IPhysicalKeyboard` to half-row bytes
- `src/Machines.Zx80/Zx80PortBus.cs` — implements `IPortBus`; delegates keyboard reads to
  `Zx80KeyboardAdapter`, routes tape reads on bit 7 (US-204)

_Changes:_
- `Zx80Machine` constructor: instantiate `Zx80PortBus` and pass to `Cpu`

_Test cases (in `Zx80MachineTests.cs` or a new `Zx80KeyboardTests.cs`):_
1. `Keyboard_NoKeysPressed_AllBitsHigh` — construct with no keys held, `IN` any half-row,
   assert result byte `== 0xFF` (all bits high).
2. `Keyboard_HalfRow_CorrectBitLow` — for each of the 8 half-rows, press one key via the
   stub, assert the correct bit (0–4) is low in the result.
3. `Keyboard_MultipleHalfRowsSelected_CombinesResults` — select two rows simultaneously
   (both address bits low), assert pressed keys from both rows appear in the result.
4. `Keyboard_KeyInWrongRow_NotReflected` — press a key, read a different half-row,
   assert result is 0xFF (key not visible in that row).

---

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

**Implementation plan — US-203**

_ZX80 display background:_
The display file lives in RAM; its start address is stored in the two-byte `D_FILE` system
variable at `0x400C`/`0x400D`. The display file has a variable-length structure:
- Byte 0: `HALT` (0x76) — display file start marker
- Then 24 rows, each consisting of 0–32 character codes followed by a `HALT` (0x76)
  terminator. A full screen uses 32 characters per row; BASIC shortens trailing rows.
- Minimum size: 25 bytes (all rows empty — just the 25 HALT bytes).
- Maximum size: 793 bytes (25 HALTs + 768 character bytes).

The character font is embedded in the ROM at `0x0E00–0x0FFF` (8 bytes per character,
64 characters). The `I` register is permanently set to `0x0E` so the CPU's refresh
addressing always points into this font table during display generation.

_How the ZX80 display actually works (hardware mechanism):_
The CPU does not have a special display mode. The ROM's display routine causes the CPU
to literally execute the display file as instructions. Character codes stored in the display
file are fetched at addresses in the high address space (the phantom copy ≥ 0x8000). The
hardware intercepts these high-address M1 fetches and returns `0x00` (NOP) to the CPU
regardless of the actual character value, while simultaneously latching the character code
for the video shift register. When the CPU fetches a `HALT` (0x76) byte — the row
terminator — the hardware lets the real value through, so the CPU actually executes HALT.

**NMI generation**: while the CPU is HALTed (internally looping NOPs to keep the refresh
register incrementing), the hardware monitors bit 6 of the R register. When bit 6
transitions from 1→0 (the lower 7 bits of R wrap from 0x7F to 0x00), an NMI is triggered.
This wakes the CPU from HALT and the ROM's NMI handler advances to the next display line.
This cycle repeats 24 times, then the ROM exits the display routine.

**NMI is gated**: NMI is only generated when the CPU is fetching from addresses ≥ 0x8000
(the phantom display copy). During normal BASIC execution (PC in 0x0000–0x7FFF) the
NMI circuit is disabled, so R wrapping does not generate spurious NMIs.

_`RunFrame()` additions:_
- At the start of each frame, snapshot `D_FILE` from RAM at `0x400C` (little-endian word)
  for use by `RenderFrame()`.
- NMI generation: after each `Cpu.Step()`, if the CPU is halted AND `Cpu.PC >= 0x8000`
  AND the R register's bit 6 has just transitioned from 1→0, call `Cpu.TriggerNmi()`.
  Track the previous R bit 6 value to detect the falling edge.
- Run until `Cpu.TotalCycles` has advanced by 64,167 T-states.

_`RenderFrame(IVideoSink sink)`:_
- Allocate a 256×192 `uint[]` pixel buffer (ARGB32). This is decoupled from CPU
  execution — read the display file directly from RAM.
- Walk the display file in RAM starting at the address stored in `D_FILE`. Skip the
  initial HALT byte. For each of the 24 rows, read character codes until the next HALT
  terminator; pad short rows with space (0x00) to fill 32 columns.
- For each character code:
  - Extract the base code: `base = charCode & 0x3F` (lower 6 bits index the character).
  - Determine inversion: `inverted = (charCode & 0x80) != 0` (bit 7 set = inverse video).
  - Look up 8 font bytes from ROM at `0x0E00 + (base * 8)`.
  - For each font byte, expand bits to pixels: bit 7 = leftmost pixel.
    If `inverted`, swap ink and paper colours.
  - Ink = black (`0xFF000000`), paper = white (`0xFFFFFFFF`).
- Call `sink.SubmitFrame(pixels, 256, 192)`.

_What the emulator does NOT need to implement:_
- The hardware NOP substitution (bit 6 inversion on M1 fetches from ≥ 0x8000) is a
  hardware-only concern. The CPU in this emulator never fetches from those addresses
  during normal execution; the ROM drives PC into the phantom range during display scan,
  but since our `AddressDecoder` returns the same RAM content at both 0x4000 and (if
  mirrored) 0xC000, the CPU sees the real character codes. The NMI mechanism replaces
  the hardware timing signal, so no special M1 interception is needed.

_New files:_
- No new source files — all logic added to `Zx80Machine.cs`.

_Test cases:_
1. `RenderFrame_AllSpaces_ProducesWhiteFrame` — fill display file with space characters
   (0x00), call `RenderFrame()`, assert all pixels are `0xFFFFFFFF`.
2. `RenderFrame_KnownCharacter_CorrectPixelPattern` — write a character with a known
   8×8 font pattern into the display file; assert the corresponding 8×8 pixel block
   matches the expected dot pattern (bit 7 of font byte = leftmost pixel).
3. `RenderFrame_InvertedCharacter_SwapsInkAndPaper` — write a character code with
   bit 7 set (e.g. 0x80 = inverted space); assert those pixels are black (`0xFF000000`)
   rather than white.
4. `RenderFrame_CorrectDimensions` — assert submitted frame is exactly 256×192.
5. `NmiGeneration_FiredOnRBit6FallingEdge` — with CPU halted and PC ≥ 0x8000, set R
   to 0x7F, call `Step()` (which increments R to 0x00, bit 6 falls), assert NMI was
   triggered.

---

**US-204 — ZX80 tape**
As a user, I want `ITapeDevice` wired to the ROM's load/save routines via the relevant
memory-mapped I/O so that `.o` / `.80` tape images can be loaded into the emulator.
- Acceptance: test loads a known tape image and asserts that RAM contains the expected bytes
  after the ROM load routine completes.

**Implementation plan — US-204**

_ZX80 tape I/O:_
The ZX80 uses a single-bit serial interface:
- **Save**: ROM pulses the MIC output (port `0xFE` bit 3, `OUT`) to write bits to tape.
- **Load**: ROM reads the EAR input (port `0xFE` bit 6, `IN`) to read bits from tape.
  Bit 6 low = pulse present; bit 6 high = silence. (Note: some sources cite bit 7; the
  ROM samples bit 6 — verified against ZX80 ROM disassembly.)

_ZX80 tape encoding (pulse-count):_
- **0 bit**: 4 pulses — each pulse is 150 µs HIGH then 150 µs LOW (~300 µs/pulse)
- **1 bit**: 9 pulses — same 150 µs HIGH + 150 µs LOW per pulse
- Bits are transmitted **MSB first** within each byte.
- Average transfer rate: ~307 baud.
- **No leader tone** — the ROM jumps straight into sampling data pulses. There is no
  initial sync or header block.

_ZX80 tape file format:_
`.o` (also seen as `.80`) — **not `.p`**, which is the ZX81 format.
- Raw memory dump of RAM from `0x4000` upward (system variables + display file +
  BASIC program + variables).
- **No file header, no filename** — the file begins at byte 0x4000 content directly.
- Load length is determined by the E_LINE system variable at RAM offset `0x400A`
  (2 bytes, little-endian). Length = E_LINE_value − 0x4000. The `Zx80TapeAdapter`
  reads this from its own byte array after loading system variables to know when to stop.

_Implementation:_
- `Zx80PortBus.In(port)`: if EAR input is addressed, return
  `ITapeDevice.ReadBit() ? 0xFF : 0xBF` (bit 6 low = pulse detected).
- `Zx80PortBus.Out(port, value)`: if MIC output is addressed, call
  `ITapeDevice.WriteBit((value & 0x08) != 0)`.
- Provide a `Zx80TapeAdapter` that implements `ITapeDevice` and streams bits from a
  `.o` file byte array using the pulse-count encoding above (MSB first, 4/9 pulses per bit).
- `ITapeDevice.Load(Stream data)` accepts the `.o` file; the adapter buffers it and
  plays back pulses on `ReadBit()` calls.

_New files:_
- `src/Machines.Zx80/Zx80TapeAdapter.cs` — `ITapeDevice` implementation for `.o` files

_Changes:_
- `Zx80PortBus.cs` — add EAR read and MIC write routing

_Test cases:_
1. `Tape_ReadBit_ReturnsHighWhenNoTape` — with no tape device, `IN 0xFE` bit 6 is high
   (EAR = 1, no signal).
2. `Tape_ReadBit_ReturnsLowOnPulse` — stub `ITapeDevice.ReadBit()` returning false;
   assert bit 6 of `IN 0xFE` is low.
3. `Tape_WriteBit_ForwardedToDevice` — `OUT 0xFE` with bit 3 set; assert stub
   `ITapeDevice.WriteBit(true)` was called.
4. `TapeAdapter_DecodesZeroBit` — feed 4 pulses to adapter; assert `ReadBit()` returns false.
5. `TapeAdapter_DecodeOneBit` — feed 9 pulses to adapter; assert `ReadBit()` returns true.
6. `TapeAdapter_LoadFile_PopulatesRam` — load a known `.o` file via the ROM load routine
   (or directly via `ITapeDevice.Load`); assert RAM at 0x4000+ matches expected bytes.

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
