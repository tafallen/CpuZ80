# ZX Spectrum +2A / +3 — Research and Architecture

Research notes and plan for the ZX Spectrum +2A, +2B, +3 and +3B. Written before
implementation, per [AGENTS.md](../AGENTS.md).

Facts were checked against the sources at the bottom rather than extrapolated
from the 128 — and several of them differ in ways that extrapolation would have
got wrong. See §1.

---

## 1. What changes from the 128 — including three traps

The +2A/+3 look like a 128 with a disk drive. The memory system is meaningfully
different, and so is the timing.

| | 128 | +2A / +3 |
|---|---|---|
| ROMs | 2 × 16K | **4 × 16K** |
| ROM select | `0x7FFD` bit 4 | **`0x7FFD` bit 4 (low) + `0x1FFD` bit 2 (high)** |
| Paging ports | `0x7FFD` | `0x7FFD` **and `0x1FFD`** |
| All-RAM mode | none | **4 configurations**, for CP/M |
| Contended banks | 1, 3, 5, 7 | **4, 5, 6, 7** |
| Contention starts | 14,361 | **14,364** |
| Delay pattern | 6,5,4,3,2,1,0,0 | **1,0,7,6,5,4,3,2** |
| I/O contention | yes | **no — memory only** |

The three easy mistakes:

1. **Contended banks move from odd to high.** Assuming `bank & 1` — which is what
   the 128 pager does — is wrong here and would silently mis-time every program.
2. **The delay pattern is different**, not just offset. It is a genuinely
   different sequence, so it cannot be modelled by shifting the 128's.
3. **The gate array applies contention only when `MREQ` is active**, so I/O
   accesses are *not* contended on these machines. The 128 contends both.

## 2. Port 0x1FFD

Write-only. Decoded on **bit 1 reset, bit 12 set, bits 13/14/15 reset** — i.e.
`(port & 0xF002) == 0x1000`.

**`0x7FFD`'s decode is narrower on these machines than on the 128.** The 128
responds to any address with A15 and A1 reset; the +2A/+3 additionally requires
**A14 set** — `(port & 0xC002) == 0x4000`. That is not a detail: `0x1FFD` also
has A15 and A1 reset, so under the 128's rule every write to `0x1FFD` would land
in the `0x7FFD` latch as well and corrupt the bank, ROM and screen bits. A14 is
what separates them, being set for `0x7FFD` and clear for `0x1FFD`.

| Bit | Normal mode (bit 0 = 0) | Special mode (bit 0 = 1) |
|---|---|---|
| 0 | 0 = normal paging | 1 = all-RAM paging |
| 1 | motor on (+3) | config select, low bit |
| 2 | high bit of ROM select | config select, high bit |
| 3 | printer strobe | printer strobe |

## 3. Special paging (all-RAM) configurations

Selected by bits 2:1 when bit 0 is set. Introduced for CP/M, which cannot run
with ROM at `0x0000`.

| Bits 2,1 | `0x0000` | `0x4000` | `0x8000` | `0xC000` |
|---|---|---|---|---|
| 0,0 | Bank 0 | Bank 1 | Bank 2 | Bank 3 |
| 0,1 | Bank 4 | Bank 5 | Bank 6 | Bank 7 |
| 1,0 | Bank 4 | Bank 5 | Bank 6 | Bank 3 |
| 1,1 | Bank 4 | Bank 7 | Bank 6 | Bank 3 |

In special mode `0x7FFD`'s bank and ROM bits are ignored for layout purposes,
but its screen bit still selects which bank the ULA displays.

## 4. ROM selection in normal mode

A 2-bit index: low bit from `0x7FFD` bit 4, high bit from `0x1FFD` bit 2.

| ROM | Contents |
|---|---|
| 0 | 128 editor, menu, self-test |
| 1 | 128 syntax checker |
| 2 | +3DOS |
| 3 | 48 BASIC |

## 5. Memory map in normal mode

Identical in shape to the 128: ROM at `0x0000`, bank 5 fixed at `0x4000`, bank 2
fixed at `0x8000`, any bank at `0xC000`.

## 6. The +3's disk controller

An NEC uPD765A on ports `0x2FFD` (main status register, read) and `0x3FFD`
(data). The **+2A has no drive** — it is otherwise the same machine, so one
implementation covers both with the FDC optional.

Out of scope for the first pass: the machines boot to their menu without a
drive, so the FDC is a separate story rather than a prerequisite.

## 7. Architecture

New project `src/Machines.ZxSpectrumPlus3`, referencing `Machines.ZxSpectrum`
and `Machines.ZxSpectrum128`, reusing:

- `FerrantiUla5C6C` — video, beeper, keyboard, contention
- `Ay38912` — unchanged, same ports
- `ZxSpectrumVideo`, `SinclairKeyboardAdapter`, tape

New:

- `Plus3MemoryPager` — both ports, 4 ROMs, special modes, its own contention rule
- `Plus3Machine` — composition
- `UlaTiming` gains a **delay pattern**, currently a hardcoded constant in the ULA

### 7.1 Two extensions to shared code

Both touch the 48K and 128, so the existing tests are the guard and the defaults
must not move:

- **Contention pattern into `UlaTiming`.** `FerrantiUla5C6C.ContentionTable` is a
  `static readonly byte[]`. It becomes part of the timing record, defaulting to
  the existing sequence.
- **I/O contention becomes optional.** `OnPortAccess` currently always applies
  the 48K rule. The +2A/+3 contends memory only, so this needs a flag or an
  injectable rule, defaulting to today's behaviour.

## 8. Architectural critique — risks

**The two ports look like they overlap, and the hardware solves it by narrowing
`0x7FFD`.** This was written up as a risk before implementation and turned out
to be resolved in silicon: `0x7FFD` requires A14 set on the +2A/+3. Implementing
it with the 128's decode produced exactly the predicted corruption, caught by
`WritingPort1ffdDoesNotDisturbThe7ffdLatch`.

**Special mode changes what "the ROM window" means.** `Remap` is currently used
to point `0x0000-0x3FFF` at a `Rom`. In special mode it points at a `Ram`, and
that window becomes *writable*. Any code assuming the bottom 16K is read-only
will break.

**Screen bank selection is unchanged but easy to lose.** In special mode the
layout ignores `0x7FFD`'s bank bits, but bit 3 still picks the displayed bank.

**We have no +2A/+3 ROM images.** Unit tests cover everything; the end-to-end
boot test will skip, exactly as the 128's did before its ROMs arrived. Until one
is run, "it boots" is unproven — and the 128 taught us that every component
passing in isolation does not imply the composition works.

**Do not assume the 128's contention rule.** `Zx128MemoryPager.IsContended` uses
`(PagedBank & 1) != 0`. The +2A/+3 rule is `bank >= 4`, and in special mode the
contended-ness of *every* window depends on the configuration, not just
`0xC000`.

## 9. Implementation order

1. **US-471** — `UlaTiming` gains the contention delay pattern; add `Spectrum2A`.
   48K and 128 values unchanged.
2. **US-472** — I/O contention becomes optional; `Plus3` disables it.
3. **US-473** — `Plus3MemoryPager`: both ports, 4 ROMs, special modes, contention.
4. **US-474** — `Plus3Machine` composition.
5. **US-475** — `Host.ZxSpectrumPlus3` runner.
6. **US-476** — uPD765A FDC and `.DSK` images (+3 only). Separate and larger.

---

## Sources

- [ZX Spectrum +2A/2B, +3/3B — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/ZX_Spectrum_+3/2A/2B)
- [128K ZX Spectrum Technical Information — World of Spectrum](https://worldofspectrum.org/faq/reference/128kreference.htm)
- [Memory paging — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/Memory_paging)
- [Contended memory — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/Contended_memory)
- [ZX Spectrum +3 Manual, Chapter 8](https://worldofspectrum.org/ZXSpectrum128+3Manual/chapter8pt23.html)
