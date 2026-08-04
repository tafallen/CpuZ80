# ZX Spectrum 128K — Research and Architecture

Research notes and architectural plan for emulating the ZX Spectrum 128 / +2
(grey). Written before implementation, per the workflow in [AGENTS.md](../AGENTS.md).

Sources are listed at the bottom. Facts here were checked against them rather
than assumed from the 48K implementation — the 128K differs in more than paging.

---

## 1. What actually changes from the 48K

The temptation is to treat the 128K as "48K plus a paging port". It is not. Four
things change, and three of them are easy to miss.

| | 48K | 128K |
|---|---|---|
| CPU clock | 3.5 MHz | **3.5469 MHz** |
| T-states per frame | 69,888 | **70,908** |
| T-states per line | 224 | **228** |
| Lines per frame | 312 | **311** |
| Contention starts at | 14,335 | **14,361** |
| Contended addresses | `0x4000–0x7FFF` | `0x4000–0x7FFF` **plus `0xC000–0xFFFF` when an odd bank is paged there** |
| RAM | 48K flat | 8 × 16K banks |
| ROM | 16K | 2 × 16K |
| Screen source | fixed | bank 5 or bank 7 |
| Sound | beeper only | beeper + AY-3-8912 |

The contention *pattern* is unchanged: `6,5,4,3,2,1,0,0` repeating every 8
T-states, for the first 128 T-states of each drawn line.

## 2. Memory map

```
0x0000–0x3FFF   ROM      — ROM 0 (128 editor) or ROM 1 (48 BASIC), selected by bit 4
0x4000–0x7FFF   RAM      — always bank 5, always contended
0x8000–0xBFFF   RAM      — always bank 2, never contended
0xC000–0xFFFF   RAM      — any bank 0–7, selected by bits 0–2
```

Banks **1, 3, 5 and 7 are contended**. Bank 5 is contended in its fixed window at
`0x4000`; the others become contended only while paged into `0xC000`. This is the
subtle one: whether `0xC000–0xFFFF` is contended depends on runtime paging state,
so contention can no longer be decided from the address alone.

(The +2A/+3 use banks 4,5,6,7 instead. Out of scope here.)

## 3. Port 0x7FFD — paging

Write-only. Reading it returns the floating bus.

Decoded on **A15 = 0 and A1 = 0 only** — partial decoding, so the port responds
to any address matching `0xxxxxxx xxxxxx0x`, not just `0x7FFD` exactly. Getting
this wrong makes some software fail, because programs do use other addresses that
happen to match.

| Bit | Meaning |
|---|---|
| 0–2 | RAM bank paged at `0xC000` |
| 3 | Screen: 0 = bank 5 (normal), 1 = bank 7 (shadow) |
| 4 | ROM: 0 = ROM 0 (128 editor), 1 = ROM 1 (48 BASIC) |
| 5 | Paging lock — once set, **all further writes are ignored until reset** |

Bit 5 is a latch, not a level: setting it disables paging permanently. The 48K
BASIC ROM sets it, which is how the machine enters "48K mode". Reset is the only
way out.

## 4. AY-3-8912

Three square-wave channels, one noise generator, one envelope generator, 16
registers.

| Port | Direction | Purpose |
|---|---|---|
| `0xFFFD` | write | select register (0–15) |
| `0xFFFD` | read | read selected register |
| `0xBFFD` | write | write to selected register |

Decoded on A15 and A14. Both ports have **A15 high**; A14 picks between them —
register select is A15=1 A14=1 (0xFFFD), data write is A15=1 A14=0 (0xBFFD).

(Several online summaries state the data port as "A15=0, A14=1". That is wrong:
0xBFFD is `1011 1111 1111 1101`, so A15 is 1 and A14 is 0. A decoding test caught
this during implementation.)

## 5. Architecture

### 5.1 Where the code goes

New project `src/Machines.ZxSpectrum128`, referencing `Machines.ZxSpectrum`, the
same way `Machines.Zx81` references `Machines.Zx80`. The 128K reuses the ULA,
video, keyboard, tape and beeper; it adds a pager and an AY.

### 5.2 Timing must become data, not constants

`FerrantiUla5C6C` currently hardcodes `CyclesPerLine = 224` and
`VisibleStartLine = 64`. These become a `UlaTiming` record passed to the
constructor, with the 48K values as the default so existing behaviour and all 46
Spectrum tests are unaffected.

```
UlaTiming(CyclesPerLine, ContentionStart, ContentionEnd, FrameCycles)
```

### 5.3 Contention becomes paging-aware

`ApplyContention` currently tests `address >= 0x4000 && address <= 0x7FFF`. On the
128K it must also contend `0xC000–0xFFFF` when the bank paged there is odd. The
ULA therefore needs to ask the pager, not just look at the address.

Cleanest split: the pager exposes `bool IsContended(ushort address)`, and the ULA
takes an optional delegate/interface for that, defaulting to the 48K rule. This
keeps the ULA ignorant of banking while letting the 128K inject its rule.

### 5.4 Paging uses `AddressDecoder.Remap`

Each write to `0x7FFD` re-points two windows:

- `0x0000–0x3FFF` → the selected ROM
- `0xC000–0xFFFF` → the selected RAM bank

`Remap` replaces rather than merges, and costs one table write per 256-byte page
(64 per 16K window), so a page switch is ~170–380 ns rather than the ~90 µs the
old flat decoder would have taken. This is why the decoder was reworked first.

### 5.5 Banks are separate `Ram` instances

Eight `Ram(0x4000)` objects rather than one `Ram(0x20000)` with offsets. Each is
mapped whole, so `Remap` sets a uniform page range and offsets stay simple
(`address - baseAddress`). It also makes "which bank is this?" a reference
comparison, which the screen and contention logic both need.

### 5.6 Video reads from a selected bank

`ZxSpectrumVideo` takes a `Ram` today and reads at fixed offsets. For the shadow
screen it must render from bank 5 *or* bank 7. Change it to hold a reference that
the machine can re-point on a `0x7FFD` write.

Note the screen bank is chosen by the **ULA**, independently of what the CPU has
paged at `0xC000` — a program can display bank 7 while writing to bank 0.

## 6. Architectural critique — risks and open questions

**The 48K contention start may already be off by one.** Reference sources say
contention begins 14,335 T-states after the interrupt on the 48K. The current
implementation derives it as `VisibleStartLine (64) × CyclesPerLine (224)` =
**14,336**, and `ContentionTests` pins that value. Sources also differ on whether
they count from the interrupt or from the first displayed pixel, so this may be a
definitional difference rather than a bug. **Do not silently "fix" it** — verify
against a timing test suite before touching it, because 46 tests and the recorded
contention figures depend on the current value. Logged as a follow-up rather than
changed as a side effect of this work.

**Contention on `0xC000` is a behaviour change to shared code.** Making
`ApplyContention` consult the pager touches the 48K path. The default must remain
exactly the address test it does today, and the existing contention tests are the
guard.

**The floating bus differs on the 128K** and is reportedly not present in the
same form on the +2A/+3. The current `ComputeFloatingBus` assumes the 48K screen
layout at a fixed address. With a shadow screen it must follow the displayed
bank. Low priority — few programs rely on it — but it should not be left silently
wrong after the effort spent fixing it.

**AY register read-back is not just a mirror.** Some registers have unused bits
that read back as 0 regardless of what was written. Worth testing against a real
register map rather than assuming write/read symmetry.

**Scope discipline.** The +2A/+3 change the paging model again (second paging
port `0x1FFD`, special all-RAM configurations, different contended banks). This
document and the stories under it cover the 128 / +2 grey only.

## 7. Implementation order

1. **US-451** — `UlaTiming` extracted, 48K values as default. No behaviour change.
2. **US-452** — `Zx128MemoryPager`: the `0x7FFD` latch, bank selection, paging
   lock, driving `AddressDecoder.Remap`. Testable standalone.
3. **US-453** — `Zx128Machine` composition: 8 banks, 2 ROMs, 128K timing, boots.
4. **US-454** — paging-aware contention.
5. **US-455** — shadow screen (bit 3).
6. **US-456** — AY-3-8912.
7. **US-457** — `Host.ZxSpectrum128` runner.

Each is independently testable; 1 and 2 carry no risk to the 48K machine.

---

## Sources

- [ZX Spectrum 128 — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/ZX_Spectrum_128K)
- [Memory paging — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/Memory_paging)
- [Contended memory — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/Contended_memory)
- [AY-3-8912 — Sinclair Wiki](https://sinclair.wiki.zxnet.co.uk/wiki/AY-3-8912)
- [ZX Spectrum Timing Tests Information](https://zxspectrum4.net/op_timing.php)
- [ZX Spectrum 128 Service Manual](https://spectrumforeveryone.com/wp-content/uploads/2017/11/ZX-Spectrum-128-Service-Manual.pdf)
- [Video parameters — zxdesign.info](http://www.zxdesign.info/vidparam.shtml)
