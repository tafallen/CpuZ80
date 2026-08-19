# Chip Implementation Status

Which chips across the emulated machines are complete, and which are partial.

Written after an audit of the source, not from recollection — every "partial"
below names the specific registers or commands that are missing.

**The common thread: every gap sits in a part of a chip that the boot ROM never
touches.** The CPC reaches its BASIC prompt without needing CRTC sync registers,
PPI direction bits or FDC timing, so "it boots" gives no signal on any of them.
The same is true of the Spectrum machines. A booting machine is evidence about
the boot path and nothing else.

Last audited: 2026-08-18.

> The first pass of this audit missed `ZxSpectrumTapeAdapter`, which has the
> same empty `WriteBit` that `SinclairTapeAdapter` had. Grepping for one class
> by name found one class; the lesson is to enumerate implementations of the
> interface instead.

---

## Summary

| Chip | Machines | Status | What's missing | Risk |
|---|---|---|---|---|
| **`Mc6845`** (CRTC) | CPC | **Complete** as a standard MC6845 | — | The later MC6845*1 and UM6845R also read back R12/R13 and have a status register. Deliberately not modelled: software identifies the CRTC type by exactly those reads |
| **`Ppi8255`** | CPC | **Complete** | — | — |
| **`Upd765a`** (FDC) | +3, CPC (planned) | **Partial** — 9 of ~15 commands | Read Track, Scan Equal/Low/High. Read Deleted aliased to Read Data (skip bit ignored); Write Deleted aliased to Write Data (no control mark set). Format fills existing sectors rather than reshaping the track, so geometry cannot change. **No timing at all** — RQM always set, instant seeks, no rotational latency | Medium — blocks copy-protected and timing-dependent disks |
| **`Ay38912`** (PSG) | 128, +2, +3, CPC | **Complete** | — | — |
| **`CpcVideo`** | CPC | **Complete** | — | — |
| **`SinclairTapeAdapter`** | ZX80/81 | **Complete** | — | — |
| **`ZxSpectrumTapeAdapter`** | Spectrum family | **Complete** | — | — |
| **`AmstradGateArray`** | CPC | **Complete** for the 464/6128 | RMR2, which belongs to the Plus's 40489 ASIC, not this chip | None for these models |
| **`CpcMemory`** | CPC | Complete | — | — |
| **`Zx128MemoryPager`** | 128, +2 | Complete | — | — |
| **`Plus3MemoryPager`** | +2A, +3 | Complete | — | — |
| **`DiskImage`** (`.DSK`) | +3, CPC | Complete | — | Never tested against a real image — see US-477 |
| **`FerrantiUla5C6C`** | Spectrum 48K/128/+3 | Complete | — | — |
| **`FerrantiUla2C158E`** | ZX80 | Complete | — | — |
| **`FerrantiUla2C184E`** | ZX81 | Complete | — | — |

---

## Notes

### `Mc6845`

The research doc ([amstrad-cpc.md](./amstrad-cpc.md) §10) called out that a
renderer with fixed geometry "will show the BASIC prompt and then fail on
anything that reprograms the CRTC". That was half-heeded: `CpcVideo` does take
its width, height, row height and start address from the CRTC, so hardware
scrolling and screen-size changes work. What it does not do is derive *timing*
from the CRTC — the scanline period is a constant in `CpcMachine`, so raster
splits and any effect synchronised to a reprogrammed sync position will be
wrong.

Fixing this properly means driving `RunFrame` from the CRTC's own counters
rather than a fixed T-state budget, which is a larger change than adding the
missing registers.

### `ZxSpectrumTapeAdapter` — now complete

`WriteBit` was an empty stub, so a SAVE produced nothing at all. Recording now
decodes MIC edges against the same pulse widths playback uses — 2168 pilot,
667/735 sync, 855 and 1710 for data bits — reassembles them into `.TAP` blocks
and writes a valid file. `zxspec --save-tape <path>` writes it on exit.

Building it exposed a robustness problem worth recording. A 1 bit is 1710
T-states and a pilot pulse is 2168, so a percentage tolerance around the pilot
reaches down over the bit: at 20% the boundary sat **24 T-states** above a 1 bit.
Exact-width tests passed by luck, and the smallest jitter turned a data bit into
a new pilot tone and silently truncated the block. The split is now the midpoint
between the two, and there is a test that feeds deliberately jittered pulses.

### `Mc6845` — complete

Finished on 2026-08-19. The cursor (R10/R11 for the scanline span and blink
mode, R14/R15 for the address), the light pen (R16/R17, latched by a strobe)
and interlace (R8) are all implemented, and the readback rules now match the
part: R14 and R15 are read/write, R16 and R17 read-only, everything else
write-only.

Two things are deliberately absent rather than missing. A standard MC6845 has
**no status register**, so returning zero is the part's behaviour; and R12/R13
do **not** read back on it. Both are features of the later MC6845*1 and the
UM6845R that some CPCs shipped instead, and software identifies which CRTC is
fitted by reading exactly those registers — so implementing them here would make
this chip claim to be a different one.

Writing these tests found a real bug: R10 was masked to five bits, which
silently discarded the cursor blink mode in bits 6-5 and made every cursor
steady. It is a seven-bit register.

None of this changes what the CPC draws. The cursor and light pen pins are not
connected on this machine — the firmware draws its own cursor in software — so
this is the chip behaving correctly rather than the machine looking different.

### `Mc6845` — timing now comes from the CRTC

The frame loop used to run on two constants: a 256 T-state scanline and an
80,000 T-state frame. Geometry already followed R1, R6, R9 and R12/R13, so the
picture was right, but *time* did not — a program reprogramming R0 or R4 for a
raster split or an overscan screen ran at the wrong rate while still looking
like it worked.

A line is now `(R0+1)` characters of 4 T-states and a frame is
`(R4+1)(R9+1)+R5` scanlines, with VSync placed by R7 and sized by R3's high
nibble. The standard setup comes to 312 lines of 256 T-states, which is 79,872
per frame rather than the 80,000 that was assumed — and the real ROM's boot
screen renders pixel-identically either way, which is exactly why the constant
survived so long.

Two details worth keeping: R3's VSync width of zero means sixteen lines, not
none, and the per-line target is carried rather than measured from the current
cycle count so a line's final instruction overshooting cannot accumulate into a
slow clock.

What remains is genuinely unused on this machine: interlace, the hardware
cursor (the CPC draws its own in software) and the light pen.

### `CpcVideo` — now complete

Mode 3 was previously derived by reasoning rather than checked, and untested.
Checking it against the published layout — `A0 B0 x x A1 B1 x x` from bit 7 down
— confirmed the derivation was right: each pixel keeps mode 0's index bits 0 and
1, from byte bits 7 and 3. Taking mode 0's *top* two bits instead would have been
equally plausible and completely wrong, so it is now pinned by tests.

The real gap was the border, which was two hardcoded constants. The border is
whatever the display does not cover, so its size has to come from the CRTC:
with it fixed, any non-standard geometry — a narrower screen, a taller one,
overscan — rendered in the wrong place on the canvas while still looking
plausible. The origin is now derived, which leaves the standard 40x25 screen
pixel-identical and puts everything else where it belongs.

One invariant makes this work: a CRTC character is always 16 canvas pixels wide
whatever the mode, because two display bytes times the mode's pixels-per-byte
times its scale always comes to sixteen.

### `SinclairTapeAdapter` and `AmstradGateArray` — now complete

Both were finished on 2026-08-18.

The tape adapter turned out to be more partial than this table first said. As
well as the missing save path, playback advanced one signal level per
`ReadBit` call, so the pulse rate depended on how often the ULA happened to
poll rather than on elapsed time. Tape encoding is entirely about durations, so
that could never have loaded a real file — and because no test ever loaded one,
nothing caught it. Playback is now timed from the CPU clock (150 us half-pulses,
1300 us bit gaps, four pulses for a 0 and nine for a 1) and saving decodes the
same encoding in reverse.

Recording needed a timed `WriteBit` overload on `ITapeDevice`, defaulted to the
untimed call so nothing else had to change: a bare level with no timestamp
cannot be decoded at all.

The Gate Array was missing two behaviours, both of which matter for raster
effects rather than for booting: a mode written to RMR now takes effect at the
next HSync rather than immediately, and VSync resynchronises the interrupt
counter two HSyncs later, suppressing the interrupt when bit 5 is already set.

### `Ay38912` — now complete

Finished on 2026-08-19. I/O port A takes an attachable source and sink, with
direction from register 7 bit 6: an input reads the pins, an output reads back
its latch and drives whatever is attached. An unconnected input floats high, as
it did before.

This turned out to be a structural fix as well as a chip one. The CPC keyboard
is wired to the **PSG's** port A, with only the row selected by the PPI — but the
PPI had been intercepting register 14 and returning the matrix itself, which is
not where that wire goes. The keyboard now hangs off the PSG, the PPI simply
passes data through, and the end-to-end typing test still passes: proof that the
CPC firmware really does configure port A as an input, which nothing had
previously checked.

"Mono only" is struck from the table rather than fixed, because it was never a
gap in the chip. The AY has three independent analogue outputs and mixing them
to one is something the machine does — both the 128 and the CPC wire them
together. `Render` gained an overload that keeps the channels apart for a host
that wants the ACB or ABC stereo those machines never had.

The 8912 bonds only port A. Port B's register is on the die and behaves, but the
package brings out no pins for it, so anything attached there is attached to
nothing.

### `Ppi8255` — now complete

Finished on 2026-08-19. Every direction bit is honoured — port A, port B and
port C's two halves independently — a mode-set word resets the output latches as
the datasheet specifies, and modes 1 and 2 drive their handshake lines on the
documented port C bits: PC4/PC5/PC3 for group A input, PC7/PC6/PC3 for group A
output, PC2/PC1/PC0 for group B, and all five for mode 2.

Directions turned out to matter on a CPC after all. Port A is the PSG's data bus
in **both** directions, so the firmware turns it round between naming a register
and reading it back. Two of this repo's own tests read the keyboard without ever
configuring port A as an input, which only worked because the direction bits were
being ignored — the real firmware had been doing it correctly all along, which is
why the end-to-end typing test kept passing while the synthetic ones broke.

Modes 1 and 2 are the part behaving correctly rather than the machine doing
anything new: the CPC connects nothing to the handshake lines and uses mode 0
throughout.

### `Upd765a` — now complete

Finished on 2026-08-19. Read Track, all three Scan variants, and proper Read
and Write Deleted Data with the skip bit. Format Track gained the execution
phase it was missing: the CPU supplies C, H, R and N for every sector, so a
format can now change a track's geometry rather than only refilling the sectors
that were already there.

**Timing is opt-in.** Given a `Clock` the controller models seek times from the
step rate Specify sets, and delivers bytes at the disk's 250 kbit/s data rate;
without one it completes instantly, as before. Opt-in because a controller that
suddenly takes time breaks every caller that polls without advancing a clock —
one of this repo's own tests did exactly that, reading the data port in a tight
loop and getting 0xFF. Real driver code polls the status register, and that test
now does too. The +3 wires the clock up; the boot path is unaffected.

Building it exposed a bug in the existing code: the sector's recorded ST2 was
passed straight through to the result, so a deleted sector always reported the
control mark — even to Read Deleted Data, which had asked for exactly that. The
mark reports a mismatch between the data mark found and the command, not the
state of the sector alone.

The old Format Track test asserted the result bytes arrived straight after the
command, which is what a controller with no execution phase does. It was
encoding the simplification rather than the hardware, and has been rewritten.
