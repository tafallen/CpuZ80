# CpuZ80 Backlog

## Goal

Emulate the Z80 CPU accurately enough to run 80s computer OS ROMs (CP/M, ZX Spectrum, etc.).
The CPU core is the first milestone. Hardware emulation follows.

## Repository strategy

This repo (`CpuZ80`) and its sibling (`Cpu6502`) share the same composable architecture and
`Machines.Common` interfaces. Platform adapters (e.g. `Adapters.Raylib`) are **copied** into
each repo rather than shared via cross-repo project references. The intent is to eventually
extract `Machines.Common` and the adapters into a third shared repo; until that exists, keep
copies in sync manually. Do not add cross-repo project references.

> ⚠️ **The copies are currently out of sync** — see [FU-001](#fu-001--port-the-rgba32-ivideosink-contract-to-cpu6502).

---

## Z80 CPU — Remaining Work

### Interrupt Handling
- ~~`INT` (maskable interrupt): modes 0, 1, and 2 — none are implemented~~ ✓
- ~~`NMI` (non-maskable interrupt): jumps to 0x0066, pushes PC — not implemented~~ ✓
- ~~IFF1/IFF2 are tracked but never acted on~~ ✓

### Undocumented Opcodes
- ~~`SLL` (CB 30–37) is implemented but classed as undocumented~~ ✓ (correctly implemented)
- ~~Several DD/FD prefix opcodes that fall through to the base table~~ ✓ (resolved via explicit prefix dispatch tables)
- ~~`IN F, (C)` (ED 70) — stores to wrong register~~ ✓ (value discarded correctly, flags set correctly)

### Accuracy Gaps
- ~~`R` register double-increment for prefixed instructions~~ ✓ (`Fetch()` is called once per byte fetched, which is correct Z80 behaviour)
- ~~Undocumented flags (bits 3 and 5 of F) are set for most instructions but
  not verified against the full ZEXALL undocumented suite~~ ✓ (100% pass confirmed via MEMPTR/WZ implementation)
- ~~`EI` one-instruction interrupt delay — INT was accepted immediately after EI~~ ✓
- ~~Invalid `ED` opcodes threw exceptions instead of acting as NOPs~~ ✓
- ~~`R` register did not increment during HALT~~ ✓
- ~~Interleaved M-cycle timing for complex instructions~~ ✓ (resolved via granular T-state interleaving in CodeGen)

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

### Performance & Architecture Improvements (Epic 1.5)

~~**US-151 — O(1) Memory Routing**
As a developer, I want the `AddressDecoder` to use an O(1) page table instead of O(N) list iteration — so that emulation speed remains constant as mapping complexity increases.
- Acceptance: tests verify correct routing for page-aligned (256-byte) mappings; unaligned mappings throw `ArgumentException`.~~ ✓

**US-152 — Instruction Code Generator**
As a developer, I want a code generator that produces a high-performance `switch` dispatcher — so that instruction execution bypasses delegate overhead and benefits from JIT jump tables.
- [x] Generator framework (`CpuZ80.CodeGen`) implemented.
- [x] "Hot path" instructions (`NOP`, `ADD A, r`) migrated to `StepGenerated()`.
- [x] All 8-bit and 16-bit instructions migrated.
- [x] Prefixed instructions (`CB`, `ED`, `DD`, `FD`) migrated with silicon-accurate T-state interleaving.
- [x] Implemented wait-state support (`WaitCycles`) for cycle-perfect synchronization.
      A `WaitPin` property was also added here but removed later: `Tick` spun on it
      with nothing able to clear the pin, so asserting it hung the emulator, and
      nothing ever set it. `WaitCycles` covers the contention use case.
- [x] Abstracted I/O port timing via `PortTick(n)` for custom hardware contention.

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

---

~~**US-202 — ZX80 keyboard matrix**
As a user, I want the ZX80 keyboard (8 half-rows × 5 keys) decoded from `IPhysicalKeyboard`
and returned via `IN` reads on the lower address bus — so that key presses are visible to
the ROM BASIC interpreter.
- Implementation: `IPortBus` inner class inside `Zx80Machine`; address lines A8–A15
  select the half-row, result byte has bits 0–4 low for pressed keys (active low).
- Acceptance: tests drive `IPhysicalKeyboard` stubs and assert the correct `IN` result byte
  for each half-row.~~ ✓

---

~~**US-203 — ZX80 display (software vsync)**
As a user, I want `RenderFrame()` to accept an `IVideoSink` and produce the ZX80's
software-generated 256×192 display — the CPU writes character data to RAM and the display
is built by scanning display RAM during `RunFrame()` — so that the screen updates at ~50 Hz.
- Implementation: `RunFrame()` detects HALT (end-of-line sync) and counts scan lines;
  `RenderFrame()` walks the display file in RAM, reads 8-pixel dot rows from the ROM
  character table, and submits an ARGB32 frame to `IVideoSink`. NOP-mapper logic
  (bit 6 inversion) applied inline during display scan. No separate chip class.
- Acceptance: integration test with a minimal ROM that writes a known character to display
  RAM; `RenderFrame()` produces the expected pixel pattern.~~ ✓

---

~~**US-204 — ZX80 tape**
As a user, I want `ITapeDevice` wired to the ROM's load/save routines via the relevant
memory-mapped I/O so that `.o` / `.80` tape images can be loaded into the emulator.
- Acceptance: test loads a known tape image and asserts that RAM contains the expected bytes
  after the ROM load routine completes.~~ ✓

---

### Epic 3 — ZX81 Machine

The ZX81 is a functional evolution of the ZX80. Its most significant change is the ULA's ability to generate NMIs to synchronize video generation with the CPU, allowing the "SLOW" mode where programs continue to run during display.

~~**US-301 — Machines.Zx81 project skeleton**
Establish the Sinclair ZX81 machine compositor. This story sets up the foundational hardware wiring, including the expanded 8K ROM and the standard 1K RAM (with partial decoding mirrors). It ensures the CPU initializes in the correct state for the ZX81's specific ROM firmware requirements.
- **Tasks**:
    - Create `Machines.Zx81` and its unit test project.
    - Configure `AddressDecoder` for partial decoding: ROM range `0x0000–0x1FFF` (mirrored at `0x2000`); RAM range `0x4000–0x43FF` (mirrored throughout `0x4000–0x7FFF`).
    - Implement `Reset()`: set `PC = 0x0000`, `I = 0x1E` (font at `$1E00`).
    - Wire `Cpu` with bus and default null `ICpuHost`.
- **Acceptance**:
    - `Zx81Machine` instantiates with 8K ROM.
    - `Reset()` sets `PC == 0` and `I == 0x1E`.
    - `Step()` and `RunFrame()` advance T-states correctly (65,000 T-states per frame).~~ ✓

~~**US-302 — ZX81 ULA: SLOW/FAST Mode & NMI**
Model the core innovation of the ZX81 ULA: the Non-Maskable Interrupt (NMI) generator. This allows the machine to operate in "SLOW" mode (continuous display) or "FAST" mode (display blanked during execution). The emulator must monitor I/O port writes to toggle this generator and inject NMIs at precise intervals.
- **Tasks**:
    - Implement `Zx81CpuHost : ICpuHost`.
    - Handle `OnPortAccess`: `OUT 0xFD` (FAST, disable NMI), `OUT 0xFE` (SLOW, enable NMI).
    - Implement `OnMemoryAccess` for high-bit display interception.
    - In `RunFrame`, inject NMI every **207 T-states** if `NmiEnabled` is true.
- **Acceptance**:
    - Port writes correctly toggle `NmiEnabled` in machine state.
    - Periodical NMIs delivered in SLOW mode; zero NMIs in FAST mode.
    - CPU halts correctly when entering NMI handler for video.~~ ✓

~~**US-303 — ZX81 Video Rendering**
Implement the video generation subsystem for the ZX81. Unlike the ZX80's fixed-length display file, the ZX81 supports "collapsed" display files where lines can be shorter than 32 characters to save precious RAM. Rendering must parse this format and handle inverse video (Bit 7).
- **Tasks**:
    - Implement `Zx81Video` using composition.
    - Parse `D_FILE` (pointer at `$400C`), stopping rows at `HALT` (0x76).
    - Pixel generation: lookup 8x8 dot patterns from ROM at `$1E00` base, applying bit 7 inversion.
    - Convert to ARGB32 and submit to `IVideoSink`.
- **Acceptance**:
    - `RenderFrame()` generates pixel-perfect images for standard and collapsed display files.
    - Inverse characters render correctly.
    - Accurately uses `I` register offset for font lookup.~~ ✓

~~**US-304 — ZX81 Keyboard & Tape**
Implement user input and persistent storage support for the ZX81. This involves wiring the 8x5 keyboard matrix and supporting the `.p` tape file format, which is the ZX81's standard memory snapshot format.
- **Tasks**:
    - Reuse `Zx80KeyboardAdapter` matrix logic.
    - Map Port `$FE` to keyboard adapter.
    - Implement `Zx81TapeAdapter` for `.p` files (RAM dump from `$4000` to `E_LINE` at `$400A`).
    - Implement bit-count encoding (EAR on bit 6, MIC on bit 3).
- **Acceptance**:
    - `IN 0xFE` reflects host key states.
    - Loading `.p` file correctly populates RAM; machine continues BASIC execution.~~ ✓

**US-305 — ZX81 16K RAM Pack**
Model the iconic 16K RAM expansion module. This disables the 1K internal RAM mirrors and provides a contiguous 16K block of memory at `$4000-$7FFF`.
- **Tasks**:
    - Add `is16K` option to `Zx81Machine` constructor.
    - If enabled, map a 16K `Ram` instance to the full `$4000-$7FFF` range.
    - Disable partial decoding mirrors that would otherwise appear in this range.
- **Acceptance**:
    - Machine detects 16K RAM via standard BASIC `PEEK` tests.
    - High-RAM software (e.g. 3D Monster Maze) executes without corruption.

---

### Epic 4 — ZX Spectrum 48K

The ZX Spectrum introduces color attributes, maskable interrupts, beeper audio, and complex ULA memory contention. This epic focuses on the 48K model.

~~**US-401 — Machines.ZxSpectrum project skeleton**
Establish the Sinclair ZX Spectrum 48K machine compositor. This wires the 16K ROM and 48K RAM into a contiguous 64K address space and initializes the CPU state for the Spectrum ROM.
- **Tasks**:
    - Create `src/Machines.ZxSpectrum` and `tests/Machines.ZxSpectrum.Tests`.
    - Configure `AddressDecoder`: ROM `0x0000–0x3FFF`, RAM `0x4000–0xFFFF`.
    - Implement `Reset()`: set `PC = 0x0000`, `I = 0x3F` (standard ROM font).
- **Acceptance**:
    - `ZxSpectrumMachine` instantiates correctly.
    - `Reset()` sets `PC == 0` and `I == 0x3F`.
    - `ReadMemory` returns ROM bytes at `$0000` and RAM bytes at `$4000`.~~ ✓

~~**US-402 — Spectrum Video & Attribute Rendering**
Implement the 256x192 attribute-based display. The Spectrum uses a bitmap (`$4000-$57FF`) and a color attribute buffer (`$5800-$5AFF`). Rendering must handle Ink, Paper, Bright, and Flash bits.
- **Tasks**:
    - Create `ZxSpectrumVideo` using the composition pattern.
    - Implement the bit-to-pixel expansion with attribute color lookup (16 colors).
    - Support the `Flash` bit (toggles every 16 or 32 frames).
    - Model the border color (captured from Port `$FE` writes).
- **Acceptance**:
    - `RenderFrame()` produces a 256x192 ARGB32 image.
    - Color attributes correctly applied to 8x8 pixel blocks.
    - Integration test verifies correct color output for known bitmap/attribute data.~~ ✓

~~**US-403 — 50Hz Interrupt Timing**
Implement the ULA's 50Hz maskable interrupt (INT) signal. This drives the ROM's keyboard scanning and flash timing.
- **Tasks**:
    - In `RunFrame`, assert the Z80 `INT` line once per frame (~69,888 T-states).
    - Ensure the INT signal is held long enough for the CPU to sample it (approx 32 T-states).
    - Model Interrupt Mode 1 (jumps to `$0038`).
- **Acceptance**:
    - The CPU jumps to `$0038` periodically during execution.
    - ROM system variable `FRAMES` (`$5C78`) increments correctly over time.~~ ✓

~~**US-404 — Beeper Audio**
Implement the single-bit speaker output. The Spectrum generates audio by rapidly toggling bit 4 of Port `$FE`.
- **Tasks**:
    - Capture `OUT 0xFE` bit 4 transitions.
    - Implement an `AudioBuffer` to store speaker states with T-state timestamps.
    - Resample the bitstream to 44.1kHz signed 16-bit mono for `IAudioSink`.
- **Acceptance**:
    - Machine exposes an `IAudioSink` integration.
    - Verified by generating a fixed-frequency square wave and asserting the output samples.~~ ✓

~~**US-405 — ULA Memory Contention**
Model the Spectrum's "Contended RAM" behavior. The ULA stops the CPU when both are accessing the first 16K bank of RAM (`$4000-$7FFF`) during video generation.
- **Tasks**:
    - Implement `ZxSpectrumCpuHost : ICpuHost`.
    - In `OnMemoryAccess`, check if address is in range `$4000-$7FFF`.
    - Inject `WaitCycles` based on the current T-state relative to the frame start (ULA scanline position).
- **Acceptance**:
    - Benchmarks show correct execution slowdown in contended RAM compared to high RAM (`$8000+`).
    - Timing-sensitive code (e.g. music routines) plays at the correct pitch.~~ ✓

~~**US-406 — Snapshots & Keyboard**
Implement user input and standard Spectrum snapshot loading (.SNA).
- **Tasks**:
    - Reuse `SinclairKeyboardAdapter` (Spectrum adds "Symbol Shift" and "Caps Shift" to the same matrix).
    - Implement `LoadSnapshot(.sna)`: populates registers and RAM from a file.
    - Wire Port `$FE` bit 6 for EAR (tape input).
- **Acceptance**:
    - `IN 0xFE` correctly reads keys (including Symbol Shift).
    - Loading a `.SNA` file successfully restores a running game or program.~~ ✓

**US-407 — Spectrum Tape Support (.TAP/.TZX)**
Implement the Spectrum's specific pulse-width modulation (PWM) tape encoding. Unlike the ZX80/81, the Spectrum uses varied pulse lengths for '0' and '1' bits and includes a pilot tone.
- **Tasks**:
    - Create `ZxSpectrumTapeAdapter : ITapeDevice`.
    - Implement pilot tone, sync pulses, and data bit timing (855/1710 µs).
    - Support the standard **.TAP** block format.
- **Acceptance**:
    - ROM `LOAD ""` routine successfully loads and runs software from a `.TAP` file.

**US-408 — Kempston Joystick Interface**
Implement the most popular Spectrum joystick standard. The Kempston interface returns joystick state on Port `$1F`.
- **Tasks**:
    - Map host arrow keys/gamepad to a bit-mask (Right=0, Left=1, Down=2, Up=3, Fire=4).
    - Map Port `$1F` (all address lines ignored) to this state.
- **Acceptance**:
    - Games configured for "Kempston" respond to host input.

---

### Epic 4.5 — ZX Spectrum 128K / +2 (grey)

Research and architecture: [zx-spectrum-128.md](./zx-spectrum-128.md). Facts were
checked against the Sinclair Wiki and the 128 service manual rather than inferred
from the 48K — the 128 changes clock, frame length, line length and contention
start as well as adding paging.

Numbered US-45x to sit between Epic 4 (Spectrum 48K) and Epic 5 (CPC), which
already owns US-50x.

- [x] **US-451 — Extract `UlaTiming`**
  Frame geometry as data so the 128 (228 T-states/line, 70,908/frame, contention
  from 14,361) can share `FerrantiUla5C6C`. 48K values are the default, so no
  behaviour change; the contention pattern re-anchors to `ContentionStart`, which
  is arithmetically identical on the 48K because 14,336 is a multiple of 224.

- [x] **US-452 — `Zx128MemoryPager`**
  Port 0x7FFD: bank select, ROM select, screen select, and the one-way paging
  lock. Partial decoding on A15 = 0 and A1 = 0. Drives `AddressDecoder.Remap`,
  and exposes `IsContended` because contention at 0xC000 now depends on which
  bank is paged there.

- [ ] **US-453 — `Zx128Machine` composition**
  Eight `Ram(0x4000)` banks, two ROMs from a 32K image, 128K timing, `RunFrame`
  at 70,908 T-states. Boots to the 128 editor ROM.

- [ ] **US-454 — Paging-aware contention**
  `FerrantiUla5C6C.ApplyContention` currently tests the address alone. It needs an
  injectable rule so the 128 can contend 0xC000-0xFFFF for odd banks. The 48K
  default must stay exactly the current address test — the existing contention
  tests are the guard.

- [ ] **US-455 — Shadow screen**
  `ZxSpectrumVideo` renders from a fixed `Ram`. It must follow the pager's
  `ScreenBank` (5 or 7), which is independent of what the CPU has paged at
  0xC000. `ComputeFloatingBus` needs the same treatment.

- [ ] **US-456 — AY-3-8912**
  Ports 0xFFFD (register select / read) and 0xBFFD (data write). Three square
  channels, noise, envelope. Mix with the existing beeper into `IAudioSink`.
  Register read-back is not a plain mirror — unused bits read as 0.

- [x] **US-457 — `Host.ZxSpectrum128` runner**
  Command-line host mirroring `Host.ZxSpectrum`. Takes either `--rom` (32K) or
  `--rom0`/`--rom1` (two 16K images, as they are usually distributed).

- [x] **US-458 — Boot the 128 editor to its menu** ✅
  The 128 boots to its menu (128 logo, Tape Loader / 128 BASIC / Calculator /
  48 BASIC / Tape Tester, © 1986 Sinclair Research Ltd). Covered end-to-end by
  `Zx128MachineTests.RealRoms_BootToTheEditorMenu`, which skips when the
  gitignored ROM images are absent. Root cause in FU-005 below.

- [x] **US-460 — Close the last two ZEXALL failures** ✅
  **ZEXALL now passes completely: 67/67, zero errors.**

  - `ld (nnnn),<ix,iy>` — the indexed transform in `CpuZ80.CodeGen` rewrote the
    `H = ` / `L = ` assignment forms but not the *operand* position, so
    `Write(nn, L)` and `Write((ushort)(nn + 1), H)` matched nothing and
    `LD (nn),IX` silently stored **HL**. The mnemonic had been rewritten, which
    made the generated code look right at a glance.
  - `<rrd,rld>` — `SetLogicFlags` sets S, Z and P/V and correctly leaves Carry
    alone, but nothing cleared **H** and **N**, so both kept whatever the
    previous instruction left behind.

  Also removed the standalone `RRD()` / `RLD()` methods from `Cpu.Extended.cs`.
  They were dead code the generated dispatcher never called — and they cleared
  H and N correctly, so a correct implementation sat unused beside a broken
  generated one. Exactly the shape of the DD/FD-CB bug in FU-005.

  CI now runs the exerciser (`.github/workflows/ci.yml`). The repo had no CI at
  all, which is why AGENTS.md's ZEXALL requirement went unenforced long enough
  for two whole instruction groups to break silently.

- [x] **US-459 — AY noise and envelope generators**
  A 17-bit LFSR tapped at bits 0 and 3 for noise, and the full 32-step envelope
  with all sixteen shapes. Volume bit 4 now takes its amplitude from the
  envelope rather than reading as full volume.

  Two bugs surfaced while doing it, both affecting the 128, +2 and +3 alike:

  - **Every channel was mistuned.** The renderer rounded a sample's worth of AY
    ticks to the nearest whole tick. At 44.1 kHz a frame gives about 2.5 ticks
    per sample, and rounding that to 3 is a 20% pitch error. Now carried as a
    fraction across samples.
  - **The mixer silenced channels it should not have.** A disabled tone was
    treated as silencing the whole channel, so a noise-only voice — the usual
    way to play drums — produced nothing at all. The two sources are ANDed, and
    a disabled one sits high.

  Writing register 13 restarts the envelope even when the value is unchanged,
  which is how music drivers retrigger a note.

---

### Epic 4.6 — ZX Spectrum +2A / +2B / +3 / +3B

Research and architecture: [zx-spectrum-plus3.md](./zx-spectrum-plus3.md).
Checked against the sources rather than extrapolated from the 128 — several
details differ in ways that assuming would have got wrong.

- [x] **US-471 — Contention pattern into `UlaTiming`**
  The delay sequence was a hardcoded `static readonly byte[]` in the ULA. It is
  now part of the timing record, because the +2A/+3 uses 1,0,7,6,5,4,3,2 rather
  than a shifted 6,5,4,3,2,1,0,0. Adds `UlaTiming.Spectrum2A`.

- [x] **US-472 — I/O contention is now optional**
  The +2A/+3 gate array contends only while MREQ is active, so I/O is not
  contended. `UlaTiming.ContendsIo` defaults to true, leaving the 48K and 128
  unchanged.

- [x] **US-473 — `Plus3MemoryPager`**
  Both paging ports, four ROMs, the four all-RAM configurations, and the
  banks-4-to-7 contention rule.

  Note the `0x7FFD` decode is **narrower** than the 128's: it requires A14 set as
  well as A15 and A1 clear. Without that, every write to `0x1FFD` also lands in
  the `0x7FFD` latch and corrupts the bank, ROM and screen bits — which is
  exactly what happened when it was first implemented with the 128's rule.

- [x] **US-474 — `Plus3Machine` composition**
  Eight banks, four ROMs from a 64K image or four 16K files, `UlaTiming.Spectrum2A`,
  `Plus3PortBus`, and the pager's own contention rule injected into the ULA.

  Verified end-to-end against the real v4.1 ROM set: the machine boots to the +3
  editor menu. The +2 (grey) images in the repo are 128-architecture, so they
  became a second real-ROM boot test for `Zx128Machine` instead.

- [x] **US-475 — `Host.ZxSpectrumPlus3` runner**
  `zxplus3`, taking either a 64K image via `--rom` or four 16K images via
  `--rom0`..`--rom3`. The four are ordered by their flag rather than by position,
  so a mis-ordered command line is an error instead of a scrambled machine.

  While wiring this up the ROM images turned out to have been reorganised into
  `roms/`, which silently disarmed both real-ROM boot tests — they skip when the
  image is absent, so they still passed, in milliseconds. The finders were
  replaced with a shared `tests/TestSupport/RomLocator.cs` that searches the
  whole working copy. **A skipping test looks exactly like a passing one; check
  the duration.**

- [x] **US-476 — uPD765A floppy controller and `.DSK` images (+3 only)**
  Research and architecture: [upd765a-fdc.md](./upd765a-fdc.md).

  `DiskImage` parses both `.DSK` variants; `Upd765a` implements the three-phase
  command/execution/result state machine on ports `0x2FFD` and `0x3FFD`. Fitted
  only when asked for — without it the machine is a +2A, which is what it was
  before. The real ROM set still reaches its menu with the drive fitted, which
  the +2A path did not prove.

  Research corrected a mistake in the +2A/+3 notes: the disk motor is **bit 3**
  of `0x1FFD`, not bit 1, and the printer strobe is bit 4, not bit 3. Bit 1 is
  ignored in normal mode. Checked against the reference rather than
  extrapolated, after the same class of error twice before.

  ⚠️ **No real `.DSK` image has been loaded.** The tests build synthetic
  images byte by byte, which pins the parser and the FDC to a known-correct
  layout, but says nothing about real disks or other emulators' quirks. "+3 disk
  support works" is not yet a claim that can be made — see US-477.

- [ ] **US-477 — Load a real `.DSK` and boot a game**
  The missing proof for US-476. Needs a disk image in the working copy.

- [ ] **US-478 — Persist disk writes**
  Writes land in the in-memory image and are lost on exit. A user who saves a
  game and closes the emulator will reasonably call that a bug. The host warns,
  which is a stopgap rather than a fix.

---

### Epic 5 — Amstrad CPC 464 / 6128

The Amstrad CPC series features a 4MHz Z80A, the Amstrad Gate Array, a 6845 CRTC for video, and an AY-3-8912 for 3-channel sound. This epic focuses on the CPC 464 (64K) base model with 6128 (128K) expandability.

**US-501 — Machines.AmstradCPC project skeleton**
Establish the Amstrad CPC motherboard compositor and memory map. The CPC 464 has a complex memory layout where 64K RAM is contiguous, but the OS ROM (Lower) and BASIC/DOS ROMs (Upper) are banked in/out of the Z80's address space.
- **Tasks**:
    - Create `src/Machines.AmstradCPC` and its unit test project.
    - Configure `AddressDecoder` for 64K RAM and the banking system:
        - **Lower ROM**: 16K at `$0000-$3FFF`.
        - **Upper ROM**: 16K at `$C000-$FFFF` (supports up to 252 different ROMs via banking).
    - Implement the `Reset()` logic: PC = `$0000`, 4MHz clock frequency.
- **Acceptance**:
    - `AmstradCpcMachine` can be instantiated with OS and BASIC ROM images.
    - Tests verify that `ReadMemory` returns ROM bytes when banking is enabled, and RAM bytes when disabled.
    - CPU `TotalCycles` advances at exactly 4 T-states per µs (4.0 MHz).

**US-502 — Amstrad Gate Array: Palette, Banking & Interrupts**
Implement the custom Amstrad Gate Array (the "brain"). It manages the 27-color palette, the memory configuration, and the unique hsync-based interrupt counter.
- **Tasks**:
    - Create `AmstradGateArray : ICpuHost` chip class.
    - Implement Port `$7Fxx` decoding (I/O range `$4000-$7FFF` but practically `$7Fxx`):
        - **PEN Selection**: Select which of the 16 palette entries (or border) is being modified.
        - **Color Assignment**: Map one of the 27 hardware colors to the selected PEN.
        - **ROM/RAM Banking**: Control the visibility of Lower and Upper ROMs.
    - Implement the **HSYNC Interrupt Counter**:
        - Count HSYNC signals from the CRTC.
        - Assert `INT` every 52 scanlines.
        - Clear `INT` when the counter is reset or the CPU acknowledges.
- **Acceptance**:
    - Port writes correctly update the 16-color internal palette registers.
    - The CPU receives maskable interrupts at a frequency of 300.3 Hz (approx every 13,312 T-states).
    - Tests confirm that disabling the Lower ROM correctly exposes the underlying RAM.

**US-503 — CRTC 6845 Video Rendering**
Implement the video generation using the 6845 CRTC. The CPC utilizes the CRTC to scan memory and the Gate Array to convert those bytes into pixels based on the active mode.
- **Tasks**:
    - Create `AmstradCrtc6845` chip class.
    - Implement the 3 CPC Graphic Modes:
        - **Mode 0**: 160x200, 16 colors (4 bits per pixel).
        - **Mode 1**: 320x200, 4 colors (2 bits per pixel).
        - **Mode 2**: 640x200, 2 colors (1 bit per pixel).
    - Implement non-linear memory fetching: CPC pixels are interleaved within characters ($800 bytes per scanline offset).
    - Support the border region rendering using the Gate Array's border color.
- **Acceptance**:
    - `RenderFrame()` produces a high-fidelity 768x272 image (including border).
    - All three modes render bit-perfect patterns compared to hardware.
    - Hardware scrolling (via CRTC registers 12/13) works correctly.

**US-504 — PSG AY-3-8912 Audio**
Implement the 3-channel sound generator. The AY chip provides music, sound effects, and 8-bit I/O ports. It is accessed indirectly via the 8255 PPI.
- **Tasks**:
    - Create `Ay38912` chip class.
    - Implement 3 square-wave oscillators with 12-bit period precision.
    - Implement the noise generator and programmable envelopes.
    - Resample the 3-channel analog-mixed output to 44.1kHz 16-bit mono/stereo.
- **Acceptance**:
    - Verified by playing a `.YM` or BASIC music routine and asserting frequency accuracy.
    - The PSG's 8-bit I/O port correctly communicates with the keyboard matrix.

**US-505 — PPI 8255: Keyboard & PSG Control**
Implement the Intel 8255 Peripheral Programmable Interface. This chip acts as the bridge between the CPU and the rest of the CPC hardware.
- **Tasks**:
    - Create `Intel8255` chip class.
    - Map the three 8-bit ports:
        - **Port A**: Bi-directional data to/from the AY-3-8912.
        - **Port B**: Input for VSYNC, Tape, and Expansion/Jumper settings.
        - **Port C**: Control for Keyboard row selection and AY chip BUSDIR/BC1 signals.
    - Map the 10-row keyboard matrix.
- **Acceptance**:
    - Keyboard scanning via PPI Port C and PSG Port A correctly identifies multiple key presses.
    - VSYNC bit in Port B correctly reflects the CRTC's vertical sync state.
    - Firmware correctly initializes the sound chip through the PPI protocol.

**US-506 — Amstrad Tape Drive (.CDT)**
Implement the tape bitstreaming logic for the CPC 464. The Amstrad uses a frequency-modulated signal for tape storage, compatible with the Sinclair standard but with specific block headers.
- **Tasks**:
    - Create `AmstradTapeAdapter : ITapeDevice`.
    - Support the **.CDT** file format (based on the TZX standard).
    - Map the TAPE_IN signal to PPI Port B (bit 7) and TAPE_OUT/MOTOR to PPI Port C.
- **Acceptance**:
    - `|TAPE` and `RUN"` commands successfully load and execute programs from .CDT images.

**US-507 — FDC 765: Disk Controller (.DSK)**
Model the NEC µPD765 Floppy Disk Controller (FDC) used in the CPC 6128 and DDI-1 expansion. This is a complex command-driven chip.
- **Tasks**:
    - Create `Nec765Fdc` chip class.
    - Implement the command-state machine (Specify, Seek, Read Sector, etc.).
    - Support the **.DSK** (Extended DSK) image format.
    - Map Port `$FBxx` for FDC status and data.
- **Acceptance**:
    - `|DISC` and `CAT` commands successfully list and load files from a virtual disk image.

**US-508 — Amstrad Digital Joystick**
Implement the CPC's built-in joystick interface. Unlike the Kempston, the Amstrad joystick is part of the keyboard matrix.
- **Tasks**:
    - Map joystick directions and fire buttons to Keyboard Row 9 of the 8255 PPI matrix.
    - Support for two joysticks via Row 6 and Row 9 mapping.
- **Acceptance**:
    - Joystick input is correctly detected by both the BASIC `JOY()` function and arcade software.

---

### General

- Snapshot save/load (SNA / Z80 file formats for Spectrum)
- Cycle-accurate timing tied to a host clock source
- Debugger hooks (breakpoints, single-step, register watch)

---

## Deferred Follow-Ups

Known, deliberately parked. Each says what is wrong, why it is parked, and what
it would take to close.

### FU-001 — Port the RGBA32 `IVideoSink` contract to Cpu6502

**Status:** parked — `Cpu6502` is actively owned by someone else, so this repo
should not push changes into it unannounced.

`IVideoSink` here now specifies **RGBA32** (packed `0xAABBGGRR`; bytes in memory
R, G, B, A) instead of ARGB32. Producers' palettes were rewritten in that order
and `RaylibHost.SubmitFrame` now pins the frame and uploads it straight to the
texture, with no per-pixel conversion and no intermediate buffer. See commit
`5f2192f`.

`Cpu6502` still has the ARGB32 copy of `Machines.Common` and a `RaylibHost` that
converts per pixel.

**Consequence while parked:** the two copies of `Machines.Common` disagree about
what a frame buffer means. Moving a machine or adapter between the repos, in
either direction, produces a picture with **red and blue swapped** — and no test
in either repo would catch it, because each is internally consistent.

**To close:** in `Cpu6502`, swap R and B in every palette literal, update the
`IVideoSink` doc comment, replace the conversion loop in `RaylibHost.SubmitFrame`
with a direct upload, and update any test asserting pixel values. Greyscale and
magenta/green literals are unchanged by the swap; blue↔red and cyan↔yellow move.
Worth doing at the same time as the eventual shared-repo extraction.

### FU-002 — Re-measure `RenderFrame` on a quiet machine

**Status:** parked — needs a machine without background load, not a code change.

`Spectrum RenderFrame` has measured 222 µs, 290 µs and 370 µs across three
benchmark runs *with no code touching that path between them*. The runs were
taken on a loaded machine: StdDev reached 23% of the mean and BenchmarkDotNet
reported RatioSD up to 0.34. One earlier single-shot run even had
`AddressDecoder` beating a raw `byte[]`, which is structurally impossible.

**Consequence while parked:** we do not actually know what the video path costs.
The gains recorded for the perimeter-only border pass (1.27×) and the removal of
the pixel conversion (64.7 µs/frame) come from interleaved in-process A/B runs
and are sound, but the *absolute* `RenderFrame` figure in `BASELINE.md` is not
trustworthy, and no trend should be read from it.

**To close:** close other applications, then

```bash
dotnet run -c Release --project tests/CpuZ80.Benchmarks -- --filter '*MachineBenchmarks*'
```

Record the result in `tests/CpuZ80.Benchmarks/BASELINE.md`, replacing the entry
currently marked "NOT USABLE". Sanity check: StdDev should be a low single-digit
percentage of the mean, and `ZX80 RenderFrame` should sit near 1.9 µs.

### FU-005 — RESOLVED: the 128 now boots

**Status:** fixed. Two CPU bugs, both found by running ZEXALL rather than by
reading the ROM disassembly.

#### The real cause: DD/FD-CB instructions were silent no-ops

The code generator built these instructions with a *single* action string, then
dropped it:

```csharp
var skippedActions = inst.Actions.Skip(2).ToArray();   // Actions.Length == 1
```

Skipping two entries of a one-entry array yields nothing, so every
`BIT n,(IX+d)`, `SET`/`RES n,(IX+d)` and shift/rotate on an index register
generated a case that only burned cycles:

```csharp
case 0x40: /* BIT 0, (IX+d) */ { Tick(3); Tick(3); Tick(3); Tick(3); } break;
```

The cycle skip is deliberate — the DD/FD and CB prefix fetches are emitted by the
handler preamble — but the actions must be kept. The Spectrum ROM keeps `IY` as
its system-variable base and tests flags with `BIT n,(IY+d)` constantly, so its
branches were effectively random. That is why the editor never reached its main
loop, never armed error recovery, and died on the first error.

Fixing this also required deciding the undocumented register copy at generation
time: operand 6 is the plain `(IX+d)` form with no copy, and `regs[6]` is
`"Read(HL)"`, which is not assignable — the old code emitted it inside a dead
`if (6 != 6)` that no longer compiled once the action was actually included.

#### Second bug: 16-bit ADC/SBC did not set S or Z

`DoAdc16` and `DoSbc16` set N, H, P/V, C and the undocumented bits, but never
Sign or Zero. `SBC HL,rr` followed by `JR Z` is *the* idiomatic 16-bit
comparison, so every such comparison used a stale flag. `TEST-ROOM` — the
routine that reported "4 Out of memory" — is `SBC HL,SP` + `RET C`.

#### What this says about the earlier investigation

Hours went into tracing the ROM: the trampoline, the paging, `ERR_SP`, the stack
swaps. All of it was downstream of a CPU that silently mis-executed a whole
instruction group. **Run the instruction exerciser before reverse-engineering a
ROM.** AGENTS.md already required ZEXALL to pass; nothing enforced it.

Remaining ZEXALL failures are tracked as US-460.

### FU-003 — Stop `RaylibHost` allocating on the audio path

**Status:** parked — small, and in the adapter rather than the core.

The emulator core is allocation-free in steady state (0 bytes/frame, guarded by
`MemoryDiagnoser` and the benchmark `metrics` mode). The Raylib adapter is not.
`RaylibHost.UpdateAudio` does `short[] samples = new short[count]`
([RaylibHost.cs:65](../src/Adapters.Raylib/RaylibHost.cs)) on every iteration of
the drain loop — up to 2 KB a time, several times a frame — and then fills it by
dequeuing one element at a time from a `Queue<short>`. `SubmitSamples` likewise
enqueues one element at a time.

**Consequence while parked:** the only per-frame garbage in the running
emulator comes from the host adapter, so the core's zero-allocation property
does not survive to the actual application. It is gen0 churn rather than a leak,
so the practical effect is minor.

**To close:** hold a reusable `short[1024]` field instead of allocating per
iteration, and replace `Queue<short>` with a ring buffer so both directions can
bulk-copy spans rather than moving one sample at a time.

**Note on verification:** `RaylibHost` calls `InitWindow` and `InitAudioDevice`,
so it cannot be exercised headlessly — this change cannot be covered by the test
suite and needs a manual run with audio.

### FU-004 — Wait states from an instruction's final memory access are deferred

**Status:** parked — aggregate timing is correct; only per-instruction
attribution is affected. Pinned by a test so the behaviour cannot drift silently.

The code generator emits each M-cycle as `Tick(n)` *followed by* that cycle's
body, so a memory access is followed by the *next* cycle's `Tick`, which consumes
any wait state the host injected. When the access is the instruction's last
action there is no following `Tick`, and the wait state carries into the next
instruction.

`LD (HL),n` is the clearest case — generated as
`Tick(4); Tick(3); Fetch(); Tick(3); Write()`. With one wait state injected per
access it costs 12 T-states instead of 13, and leaves `WaitCycles == 1` pending.
Nothing is lost: the carry is paid by the following instruction. Covered by
`WaitStateTests.WaitCycles_FromFinalAccess_AreDeferredToTheNextInstruction`,
which asserts both the shortfall and that the next instruction pays it.

**Consequence while parked:** on the ZX Spectrum, contention for an instruction
ending in a memory write is attributed one instruction late. Totals over a frame
are right, so this does not affect throughput or the contention figures in
`BASELINE.md`; it would matter for cycle-exact raster effects that depend on
where within an instruction the stall lands.

**To close:** change the interleaving in
[CpuZ80.CodeGen/Program.cs](../src/CpuZ80.CodeGen/Program.cs) so each memory
access is followed by its own M-cycle `Tick`, or add an explicit wait-drain after
the final body fragment. Do **not** hand-edit `Cpu.Generated.cs`. Any change here
moves T-state accounting across the whole instruction set, so all 309 tests —
many of which assert exact `TotalCycles` — plus ZEXALL must pass afterwards, and
`ContentionTests` should be re-checked closely.
