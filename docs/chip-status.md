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
| **`Mc6845`** (CRTC) | CPC | **Partial** — 6 of 18 registers act | R2/R3 sync position and widths, R5 vertical adjust, R8 interlace, R10/R11 and R14/R15 cursor, R16/R17 light pen. Status register always returns 0. `VerticalTotal`/`HorizontalTotal` are exposed but read by nothing — the frame loop uses a hardcoded 256 T-state scanline | **High** — most software reprograms the CRTC |
| **`Ppi8255`** | CPC | **Partial** — mode 0 only | The control word is stored but never acted on: port direction bits are ignored, so port A always reads as input and port B is hardwired as input. Modes 1 and 2 (strobed I/O) absent | Low for the CPC, which uses mode 0 only |
| **`Upd765a`** (FDC) | +3, CPC (planned) | **Partial** — 9 of ~15 commands | Read Track, Scan Equal/Low/High. Read Deleted aliased to Read Data (skip bit ignored); Write Deleted aliased to Write Data (no control mark set). Format fills existing sectors rather than reshaping the track, so geometry cannot change. **No timing at all** — RQM always set, instant seeks, no rotational latency | Medium — blocks copy-protected and timing-dependent disks |
| **`Ay38912`** (PSG) | 128, +2, +3, CPC | **Near complete** | I/O ports (registers 14 and 15) stubbed to `0xFF` unless configured as outputs. Mono only, no stereo panning | Low — both machines are mono, and register 14 is intercepted by the PPI on the CPC |
| **`CpcVideo`** | CPC | **Complete** | — | — |
| **`SinclairTapeAdapter`** | ZX80/81 | **Complete** | — | — |
| **`ZxSpectrumTapeAdapter`** | Spectrum family | **Partial** — read only | `WriteBit` is an empty stub, so saving to tape silently does nothing. Playback is already T-state timed | Low, but silent — **missed by the first pass of this audit** |
| **`AmstradGateArray`** | CPC | **Complete** for the 464/6128 | RMR2, which belongs to the Plus's 40489 ASIC, not this chip | None for these models |
| **`CpcMemory`** | CPC | Complete | — | — |
| **`Zx128MemoryPager`** | 128, +2 | Complete | — | — |
| **`Plus3MemoryPager`** | +2A, +3 | Complete | — | — |
| **`DiskImage`** (`.DSK`) | +3, CPC | Complete | — | Never tested against a real image — see US-477 |
| **`FerrantiUla5C6C`** | Spectrum 48K/128/+3 | Complete | — | — |
| **`FerrantiUla2C158E`** | ZX80 | Complete | — | — |
| **`FerrantiUla2C184E`** | ZX81 | Complete | — | — |

---

## Notes on the two that matter most

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

### `Upd765a`

The absent timing is the significant part, not the absent commands. `+3DOS` and
AMSDOS both work against instant completion, but any loader that measures how
long the controller takes — which is most disk-based copy protection — will not.
Adding the missing commands is straightforward; adding timing means the FDC
needs a clock.
