# Amstrad CPC 464 / 6128 — Research and Architecture

Research and plan for Epic 5, written before implementation, per
[AGENTS.md](../AGENTS.md).

Facts here were checked against the sources at the bottom, and two of them
contradicted each other — see §2.1. The Spectrum work in this repo produced
three separate bugs from extrapolating a plausible-looking detail instead of
looking it up, so where a claim is inferred rather than sourced it says so.

---

## 1. What kind of machine this is

The CPC is not a Spectrum with different colours. Three things make it
structurally different from anything already in this repo:

1. **A real CRT controller.** The Motorola 6845 generates the display addresses.
   Screen geometry is *programmable*, not fixed, so the video circuit cannot be
   a hardcoded raster loop the way `FerrantiUla5C6C` is.
2. **The Gate Array stretches every memory access to a 4 T-state boundary.** The
   Z80 runs at 4 MHz but no access completes off a microsecond boundary, so
   instruction timing is quantised in a way the Z80 core has never had to model.
3. **I/O is decoded on the high address byte**, not the low one. Every device
   listens on a *different* address line being low or high, and several respond
   to overlapping addresses.

The 464 and the 6128 are the same architecture: 64K versus 128K, tape versus
disk, BASIC 1.0 versus 1.1. One implementation covers both, with the extra RAM
and the disk ROM optional — the same shape as the +2A/+3 relationship.

## 2. The Gate Array

Selected for I/O writes when **A15 = 0 and A14 = 1**, conventionally written
`&7Fxx`. It is write-only; there is nothing to read back.

The top two bits of the *data* byte select which of four registers is being
written:

| Bits 7-6 | Register | Purpose |
|---|---|---|
| 00 | PENR | Select which pen (or the border) the next INKR applies to |
| 01 | INKR | Assign a hardware colour to the selected pen |
| 10 | RMR | Screen mode, ROM enables, interrupt counter reset |
| 11 | MMR | RAM banking — 6128 only, and not actually in the Gate Array |

### 2.1 A contradiction in the sources, resolved

[cpctech](https://cpctech.cpcwiki.de/docs/garray.html) states that `11` selects
the screen mode and ROM configuration and does not define `10`. [Grimware's
Gate Array reference](https://www.grimware.org/doku.php/documentations/devices/gatearray)
states `10` is RMR and `11` is MMR.

**Grimware is correct** and is the numbering used above. The disagreement is
worth recording because either choice looks equally plausible from the code, and
picking the wrong one would put the machine in the wrong screen mode on the
first instruction the OS ROM executes — a failure that looks like a video bug
rather than a decoding bug, which is exactly the kind of wrong trail this repo
has been down before.

### 2.2 RMR — ROM and mode register

| Bit | Name | Meaning |
|---|---|---|
| 4 | I | 1 = reset the interrupt counter |
| 3 | UR | Upper ROM: **0 enables**, 1 disables |
| 2 | LR | Lower ROM: **0 enables**, 1 disables |
| 1-0 | VM | Video mode 0-3 |

**The ROM enables are active low.** Getting this inverted maps RAM where the OS
expects ROM, and the machine dies before it draws anything.

### 2.3 MMR — RAM banking, 6128 only

Despite arriving through the Gate Array's port, the banking on a 6128 is decoded
by a separate PAL, which is why it responds to a bit combination the Gate Array
itself leaves unused. Low three bits select one of eight configurations, where
banks 0-3 are the base 64K and 4-7 the second:

| Config | `&0000` | `&4000` | `&8000` | `&C000` |
|---|---|---|---|---|
| 0 | 0 | 1 | 2 | 3 |
| 1 | 0 | 1 | 2 | 7 |
| 2 | 4 | 5 | 6 | 7 |
| 3 | 0 | **3** | 2 | 7 |
| 4 | 0 | 4 | 2 | 3 |
| 5 | 0 | 5 | 2 | 3 |
| 6 | 0 | 6 | 2 | 3 |
| 7 | 0 | 7 | 2 | 3 |

Config 3 is the odd one: `&4000` holds base bank **3**, not bank 1. It is not a
typo in the sources and not a pattern that can be derived from the others.

This is close enough to the +2A/+3's special paging modes that
`Plus3MemoryPager` is the right thing to look at when writing it — including its
lesson that contention (here, banking) must be decided per window rather than
globally.

### 2.4 Colour

27 hardware colours, selected by a 5-bit value of which only 27 combinations are
distinct. Sixteen pens plus a border pen. The palette is fully indirect: the
screen memory holds pen numbers, and INKR maps pens to colours, so a palette
change recolours the existing display instantly.

## 3. Interrupts

The Gate Array counts **HSync falling edges**. At 52 it raises `INT` and resets
the counter, giving **300 Hz** on a PAL machine — six interrupts per 50 Hz frame,
not one. Any host loop assuming one interrupt per frame, as the Spectrum
machines in this repo do, is wrong here.

When the CPU acknowledges, bit 5 of the counter is cleared, which prevents a
second interrupt within 32 HSync periods. Software also resets the counter
deliberately via RMR bit 4 to synchronise raster effects.

## 4. Screen modes and video memory

| Mode | Resolution | Colours | Pixels per byte |
|---|---|---|---|
| 0 | 160 × 200 | 16 | 2 |
| 1 | 320 × 200 | 4 | 4 |
| 2 | 640 × 200 | 2 | 8 |
| 3 | 160 × 200 | 4 | 2 (undocumented) |

Pixel bits within a byte are **interleaved, not adjacent** — mode 0 packs its two
pixels as alternating bits rather than two nibbles. Decoding this by taking
contiguous bit groups produces a display that looks almost right, which makes it
an easy bug to ship.

The screen is not a linear frame buffer. With the default CRTC setup the address
of a character row is derived from the CRTC's start address, and each of the 8
scanlines within a row sits `&800` apart. The layout is a *consequence* of the
CRTC's address generation, not a fixed rule, so the right implementation
computes it from the CRTC rather than hardcoding the default.

## 5. I/O map — decoded on the high byte

Devices are selected by individual address lines being low, so addresses
overlap and more than one device can answer a single access. This repo's
`PortDecoder` already models exactly that, with its `LogicalAnd` conflict
policy — the same mechanism the 128 needed.

| Device | Selected when | Conventional address |
|---|---|---|
| Gate Array / RAM banking | A15=0, A14=1 | `&7Fxx` |
| CRTC 6845 | A14=0, A13=1 | `&BCxx`-`&BFxx` |
| ROM select | A13=0 | `&DFxx` |
| Printer | A12=0 | `&EFxx` |
| PPI 8255 | A11=0 | `&F4xx`-`&F7xx` |
| Floppy (6128) | A10=0 | `&FA7E`, `&FB7E` |

A9 and A8 select the register within the CRTC and the port within the PPI.

## 6. The PPI 8255 — keyboard, sound and everything else

The CPC has no direct keyboard port. An Intel 8255 PPI sits between the CPU and
both the keyboard and the AY-3-8912:

- **Port A** — the PSG data bus, and the keyboard matrix row comes back through it
- **Port B** — tape input, printer busy, refresh rate link, distributor ID, VSync
- **Port C** — PSG control (BDIR/BC1), keyboard row select in the low nibble,
  tape motor and output

Reading the keyboard therefore means writing a row number to PPI port C, setting
the PSG to read mode, and reading PPI port A. **`Machines.Common.IPhysicalKeyboard`
maps onto this fine, but nothing else in this repo reads a keyboard through a
sound chip**, so the existing `SinclairKeyboardAdapter` is not a template.

Port B bit 4 is a link that reports 50 Hz or 60 Hz, and bit 0 returns VSync,
which is how the OS synchronises. Getting VSync wrong here stalls the firmware
in a way that looks like a CPU bug.

## 7. Timing — the part with no precedent in this repo

The Gate Array gives the Z80 a 4 MHz clock but inserts wait states so that
**every memory access is aligned to a 4 T-state boundary**. The practical
consequence is that all instruction timings round up to a multiple of 4, so an
instruction the Z80 documentation calls 7 T-states takes 8 on a CPC.

The core already has `WaitCycles`, added for Spectrum contention, and this is a
different use of the same mechanism: not "this address is slow" but "this access
must finish on a boundary". FU-004 in the backlog — deferring wait states to an
instruction's final access — is directly relevant and may need doing first.

This is the single largest risk in the epic, because it is a change to how the
CPU is driven rather than a new peripheral.

## 8. ROM images

The images in `roms/Amstrad/CPC 6128` are **not raw dumps**: each carries a
128-byte AMSDOS header, which is why `Z80CPC.ROM` is 32,896 bytes rather than
32,768 and `Z80DISK.ROM` is 16,512 rather than 16,384.

| File | Payload | Contents |
|---|---|---|
| `Z80CPC.ROM` | 32,768 | OS (16K, lower) + BASIC 1.1 (16K, upper) |
| `Z80DISK.ROM` | 16,384 | AMSDOS, upper ROM 7 |

Verified by reading past the header: the OS payload opens `01 89 7F ED 49`
(`LD BC,&7F89 / OUT (C),C`, the Gate Array write that sets up the screen mode),
and the disk payload opens `01 00 05 00` — ROM type 1, background ROM, version
0.5.

The loader must detect and strip this header. Loading the file as-is puts
everything 128 bytes out of alignment and the machine executes garbage from its
first instruction. The header is identifiable: byte `&12` is the file type (2 =
binary) and the 16-bit logical length at `&18` matches the payload size.

## 9. Architecture

New project `src/Machines.AmstradCpc`:

| Component | Responsibility |
|---|---|
| `AmstradGateArray` | Pens, palette, RMR, mode, interrupt counter, video decode |
| `Mc6845` | CRTC registers and address generation |
| `Ppi8255` | The three ports and the keyboard/PSG plumbing |
| `CpcMemory` | Lower/upper ROM enables, upper ROM select, the 8 RAM configs |
| `CpcMachine` | Composition, `RunFrame`, 300 Hz interrupts |

Reused unchanged: `AddressDecoder`, `PortDecoder`, `Ram`, `Rom`, `Ay38912`
(same chip, and it now has working noise and envelopes), `Upd765a` and
`DiskImage` for the 6128's drive — **the FDC was written for the +3 but the CPC
uses the same controller and the same `.DSK` format**, which is a real reuse
rather than a coincidence.

Not reused: anything Sinclair-specific. `FerrantiUla5C6C` is the wrong shape
because its geometry is fixed.

## 10. Architectural critique — risks

**The 4 T-state alignment is a CPU-level change, not a peripheral.** Everything
else in this epic is a new class; this reaches into how instructions are timed.
It should be proven on its own, before any video work, because a timing error
underneath a half-working display is close to undiagnosable. This session
already lost hours to a Spectrum boot failure that was actually two CPU bugs.

**The CRTC makes the display programmable, so "it renders" is a weak claim.**
A hardcoded 320×200 renderer will show the BASIC prompt and then fail on
anything that reprograms the CRTC — which is most commercial software, and all
of the interesting demos. The renderer must derive geometry from the CRTC from
the start, or it will be rewritten.

**The keyboard runs through two chips.** Keyboard reads go CPU → PPI → PSG →
matrix. Three components must all be right before a single keypress registers,
and a failure in any of them looks identical from the outside. The PSG side is
already tested; the PPI is not, and should be tested standalone before being
wired up.

**Mode 0/1 pixel interleaving looks almost right when wrong.** Worth a test
against a known byte pattern rather than eyeballing the display.

**We have ROMs but no software.** As with the +3, the ROMs will prove the
machine boots to a BASIC prompt. They will not prove the CRTC, the banking or
the disk path. Getting to a prompt is the milestone; it is not "the CPC works".

**Config 3's banking layout cannot be derived.** It has to be a lookup table.
Any clever formula that reproduces configs 0-2 and 4-7 will silently get 3
wrong.

## 11. Implementation order

Epic 5 was already planned as US-501 to US-508 before this research; the
numbering below keeps it. Research changed three things: a new story goes in
front for the timing work, and two stories shrink to reuse rather than build.

1. **US-500 — 4 T-state access alignment** *(new)*. The riskiest item and the
   only one that touches the CPU core, so it goes first and is proven alone.
   Behind a flag: the Spectrum machines must be untouched, and their existing
   timing tests are the guard.
2. **US-501** — project skeleton, memory map, ROM loading **with AMSDOS header
   stripping**, and the 8 RAM configurations including config 3's quirk.
3. **US-502** — Gate Array: PENR/INKR/RMR/MMR decode, palette, mode, the 300 Hz
   interrupt counter.
4. **US-503** — CRTC 6845 and video, with geometry derived from the CRTC.
5. **US-504** — **reuse the existing `Ay38912`**, not a new class. Same chip;
   it already has working noise and envelopes.
6. **US-505** — PPI 8255 and the keyboard path through the PSG.
7. **US-506** — tape, `.CDT`.
8. **US-507** — **reuse the existing `Upd765a` and `DiskImage`**, not a new FDC.
   Same controller and the same `.DSK` format as the +3.
9. **US-508** — joystick, which is part of the keyboard matrix here.

Booting to the BASIC prompt lands after US-505. A host runner is needed to see
it and is not currently a story.

---

## Sources

- [Gate Array — Grimware](https://www.grimware.org/doku.php/documentations/devices/gatearray) — the authoritative register reference
- [Amstrad CPC Gate-Array — cpctech](https://cpctech.cpcwiki.de/docs/garray.html) — disagrees on register selection, see §2.1
- [Gate Array — CPCWiki](https://www.cpcwiki.eu/index.php/Gate_Array)
- [Standard Memory Expansions — CPCWiki](https://www.cpcwiki.eu/index.php/Standard_Memory_Expansions)
- [Understanding the Amstrad CPC Video, RAM and Gate Array Subsystem — Bread80](https://bread80.com/2021/06/03/understanding-the-amstrad-cpc-video-ram-and-gate-array-subsystem/)
- [Amstrad CPC 464/6128 Programming Resources](https://gist.github.com/neuro-sys/eeb7a323b27a9d8ad891b41144916946)
