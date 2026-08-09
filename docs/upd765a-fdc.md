# NEC uPD765A Floppy Controller and `.DSK` Images — Research and Architecture

Research and plan for US-476, the +3's disk subsystem. Written before
implementation, per [AGENTS.md](../AGENTS.md).

The +2A and +3 are the same machine apart from this. Everything below is
optional at construction: with no controller fitted the machine is a +2A, which
is what ships today and what boots.

---

## 1. The shape of the problem

The uPD765A is not a register file you can poke at — it is a three-phase state
machine, and getting the phases wrong is the classic way to produce a controller
that passes unit tests and hangs the ROM.

| Phase | Direction | What happens |
|---|---|---|
| Command | CPU → FDC | An opcode byte, then a fixed number of parameter bytes |
| Execution | either | Data transfer, if the command has any |
| Result | FDC → CPU | A fixed number of status bytes, if the command has any |

The CPU is expected to poll the **Main Status Register** between every single
byte. Software that trusts the MSR — which +3DOS does — will deadlock against an
implementation that transfers a byte at the wrong moment, even if the bytes
themselves are right.

Not every command has all three phases, and that asymmetry matters:

- `Specify` has a command phase only. **It produces no result bytes**, and an
  implementation that offers some will desynchronise the driver immediately.
- `Recalibrate` and `Seek` have no result phase either; they complete
  asynchronously and are collected later with `Sense Interrupt Status`.
- `Read Data` has all three.

## 2. Ports

| Port | Decode | Direction | Register |
|---|---|---|---|
| `0x2FFD` | `(port & 0xF002) == 0x2000` | read | Main Status Register |
| `0x3FFD` | `(port & 0xF002) == 0x3000` | read/write | Data register |

Both need A1 low, like everything else on this machine. The motor is **bit 3** of
`0x1FFD` — see [zx-spectrum-plus3.md](./zx-spectrum-plus3.md) §2, where an
earlier revision of this document had it wrong — and the drive reports not-ready
while it is off.

The motor bit is latched regardless of the paging lock, since the lock disables
paging rather than the whole port. That is an inference rather than something
the reference states, and it is the least surprising reading: a machine that
locked paging and thereby froze its disk motor forever would be a strange
design.

### Main Status Register bits

| Bit | Name | Meaning |
|---|---|---|
| 7 | RQM | Ready to transfer a byte. The CPU polls this. |
| 6 | DIO | Direction: set means FDC → CPU |
| 5 | EXM | Execution phase in progress (non-DMA) |
| 4 | CB | Controller busy |
| 3-0 | D3B-D0B | Drive n is seeking |

## 3. Commands

The opcode's low five bits select the command; the high bits are modifiers
(`MT` multitrack, `MF` MFM, `SK` skip deleted).

| Opcode | Command | Params | Results |
|---|---|---|---|
| 0x03 | Specify | 2 | **0** |
| 0x04 | Sense Drive Status | 1 | 1 (ST3) |
| 0x05 | Write Data | 8 | 7 |
| 0x06 | Read Data | 8 | 7 |
| 0x07 | Recalibrate | 1 | 0 |
| 0x08 | Sense Interrupt Status | 0 | 2 (ST0, PCN) |
| 0x0A | Read ID | 1 | 7 |
| 0x0D | Format Track | 5 | 7 |
| 0x0F | Seek | 2 | 0 |

An unknown opcode must return a single result byte of `0x80` (ST0 with the
invalid-command code). Silently ignoring it hangs the driver.

The seven result bytes of a read or write are `ST0, ST1, ST2, C, H, R, N`.

### Status register bits that actually matter here

- **ST0**: `IC` (bits 7-6: 00 normal, 01 abnormal), `SE` seek end (5), `NR` not
  ready (3), `HD` head (2), `US` unit (1-0).
- **ST1**: `EN` end of cylinder (7), `DE` data error (5), `OR` overrun (4), `ND`
  no data — sector not found (2), `NW` not writable (1), `MA` missing address
  mark (0).
- **ST2**: `CM` control mark, i.e. a deleted-data sector (6), `DD` data error in
  data field (5), `WC` wrong cylinder (4), `BC` bad cylinder (1).
- **ST3**, from Sense Drive Status: `WP` write protected (6), `RY` ready (5),
  `T0` track 0 (4), `TS` two-sided (3).

## 4. The `.DSK` image format

Two variants, both starting with a 256-byte disk header.

| Offset | Standard | Extended |
|---|---|---|
| 0x00 | `MV - CPCEMU Disk-File\r\nDisk-Info\r\n` | `EXTENDED CPC DSK File\r\nDisk-Info\r\n` |
| 0x30 | track count | track count |
| 0x31 | side count | side count |
| 0x32 | track size, 16-bit, includes the track header | unused |
| 0x34 | — | table of track sizes, one byte each, in units of 256 |

Only the first 8 bytes of the signature are worth checking; emulators have
written all sorts of things into the rest of it.

Each track is a 256-byte header followed by its sector data:

| Offset | Field |
|---|---|
| 0x00 | `Track-Info\r\n` |
| 0x10 | track number |
| 0x11 | side number |
| 0x14 | sector size code N |
| 0x15 | sector count |
| 0x17 | filler byte |
| 0x18 | sector info list, 8 bytes per sector |

Sector info is `C, H, R, N, ST1, ST2` then a 16-bit actual length. **In the
standard format those last two bytes are unreliable** and the size is `128 << N`;
in the extended format they are authoritative and are the only way to represent
weak sectors or deliberately over-long sectors used for copy protection.

A track size of 0 in the extended table means an unformatted track, which is not
the same as a track full of zeros — the FDC must report a missing address mark
rather than returning data.

### +3 disk geometry

40 tracks, single-sided, 9 sectors of 512 bytes. The sector *numbering* carries
meaning: a system-format disk numbers them `0x41`-`0x49` and a data-only disk
`0xC1`-`0xC9`. +3DOS identifies the format by reading a sector ID, so `Read ID`
has to return the real numbering from the image rather than a synthesised
1-to-9.

## 5. Architecture

New in `src/Machines.ZxSpectrumPlus3`:

- **`DiskImage`** — parses both `.DSK` variants into tracks and sectors. Pure
  data, no FDC knowledge. Round-trips writes back into its own buffers so a
  saved game persists for the session.
- **`Upd765a`** — the state machine and `IPortBus`. Holds a `DiskImage?` per
  drive; null means no disk in that drive, which is distinct from no drive.
- **`Plus3Machine`** gains an optional FDC. Absent, the machine is a +2A.

Sector lookup deliberately searches the current track for a matching `R` rather
than indexing by position, because that is what the hardware does and it is the
only behaviour that gets interleaved and non-sequentially-numbered disks right.

## 6. Architectural critique — risks

**We have no `.DSK` images, and this is the first story where that really
bites.** For the ROMs, one boot test proved the whole composition. Here the
equivalent proof is "a real game loads", and it cannot be run. The mitigation is
to *build* a valid image in the tests — a synthetic disk exercises the parser
and the FDC against a known-correct layout, and is a stronger test than a real
image for everything except compatibility with other emulators' quirks. It is
not a substitute for loading a real disk, and this should not be described as
"+3 disk support works" until one has been.

**The MSR protocol is the likely failure mode, not the commands.** A wrong ST1
bit produces a disk error the user sees; a wrong RQM/DIO transition produces a
hang with no diagnostic at all. Tests should assert the *polling sequence* — MSR
before and after every byte — not just the bytes.

**`Specify` returning result bytes is the specific trap.** It is the first
command +3DOS issues, and offering it a result byte desynchronises everything
after it. Called out here because it is easy to write a uniform "every command
has a result phase" loop.

**Write support risks silent data loss.** Writes go to the in-memory image; they
are not flushed to disk unless asked for. A user who saves a game and closes the
emulator will lose it, and will reasonably call that a bug. Either persist on
exit or make the read-only-ness explicit.

**The motor bit is shared with the paging port.** `0x1FFD` bit 1 is the motor in
normal mode but a config-select bit in special mode, so the FDC must read it
from the pager's decoded state rather than sniffing the port itself.

## 7. Implementation order

1. `DiskImage` — parse both variants, expose tracks and sectors. Tests build
   images byte by byte.
2. `Upd765a` — MSR and phase machine; `Specify`, `Sense Interrupt Status`,
   `Sense Drive Status`, `Recalibrate`, `Seek`, invalid-opcode.
3. `Read ID` and `Read Data`.
4. `Write Data` and `Format Track`.
5. Wire into `Plus3Machine` and the host's `--disk` option.

---

## Sources

- [uPD765A datasheet — NEC](https://www.cpcwiki.eu/imgs/f/f3/UPD765_Datasheet_OCRed.pdf)
- [765 FDC — CPCWiki](https://www.cpcwiki.eu/index.php/765_FDC)
- [Disk image file format (.DSK) — CPCWiki](https://www.cpcwiki.eu/index.php/Format:DSK_disk_image_file_format)
- [ZX Spectrum +3 Manual, Chapter 8](https://worldofspectrum.org/ZXSpectrum128+3Manual/chapter8pt23.html)
- [+3DOS — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/Plus3DOS)
