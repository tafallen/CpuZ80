# Amstrad CPC Cassette and `.CDT` — Research and Notes

Research for US-506, the CPC's tape interface and the `.CDT` image format.
Written alongside the implementation, per [AGENTS.md](../AGENTS.md).

---

## 1. `.CDT` is TZX

The `.CDT` format **is** the TZX format. The file signature is `ZXTape!` either
way, and only the extension distinguishes a CPC tape from a Spectrum one. Any
TZX parser reads a `.CDT` and vice versa.

## 2. The timing trap

**Every timing in the file is in Spectrum T-states at 3.5 MHz.** A CPC runs at
4 MHz, so all of them must be multiplied by 4/3.5 on the way in.

This is the single detail most likely to make the difference between a tape that
loads and one that does not, and it fails in the worst way: playing the values
unscaled makes every pulse about **14% short**, which is inside what a forgiving
loader tolerates and outside what a tight one does. Some tapes would load, some
would not, and nothing would point at why.

Each pulse is scaled and truncated on its own rather than the total being
scaled, because a pulse has to be a whole number of T-states. Summing scaled
pulses therefore differs from scaling a sum by a T-state or so per pulse — which
caught out the first version of the tests rather than the code.

## 3. Blocks

| ID | Block | Handled |
|---|---|---|
| 0x10 | Standard speed data | Yes — pilot length picked from the flag byte |
| 0x11 | Turbo speed data | Yes — the one a CPC tape normally uses |
| 0x12 | Pure tone | Yes |
| 0x13 | Pulse sequence | Yes |
| 0x14 | Pure data | Yes |
| 0x20 | Pause / stop the tape | Yes |
| 0x21, 0x22 | Group start and end | Skipped |
| 0x30, 0x31 | Text description, message | Skipped, text kept |
| 0x32 | Archive info | Skipped, title kept |
| 0x33 | Hardware type | Skipped |
| 0x35 | Custom info | Skipped |
| 0x5A | Glue block | Skipped |

**An unrecognised block is refused, not skipped.** TZX block lengths are not
self-describing from the ID alone, so skipping one means guessing where it ends;
guessing wrong plays the rest of the file as noise, which presents as a broken
tape rather than as a missing feature. Failing with the block ID and offset says
exactly what is unsupported.

### Bit encoding

Each pulse is a *half*-wave: the level alternates from one pulse to the next.
Every data bit is two pulses of the same length, short for a 0 and long for a 1.
The final byte of a block may carry fewer than eight meaningful bits, and
playing the padding appends bits the loader never expects.

## 4. The machine side

The cassette is wired through the PPI, and the bit assignments are not the
obvious ones:

| Line | Where |
|---|---|
| Cassette read data | Port B, bit 7 |
| Cassette write data | Port C, **bit 5** |
| Cassette motor | Port C, **bit 4** |

Motor on bit 4 and data on bit 5, that way round. The natural guess is the
reverse, and it leaves a 464 unable to load anything while the deck appears to
run — checked against the reference rather than assumed.

**The read line is sampled per scanline**, not per frame. The firmware measures
how long a level holds in order to tell a 0 from a 1, so a line that only
updates fifty times a second carries no data at all.

## 5. What is not done

- **Saving to `.CDT`.** Playback only. The interface carries writes to the tape
  device, and `SinclairTapeAdapter` and `ZxSpectrumTapeAdapter` both show the
  decoding pattern, but nothing writes a `.CDT` file yet.
- **Block 0x15, direct recording.** Rare, and a different shape from the rest:
  a sampled waveform rather than a pulse list.
- **No real `.CDT` has ever been loaded.** The tests build images byte by byte,
  which pins the parser to the documented layout but says nothing about real
  files. The same standing caveat as US-477 for disks.

---

## Sources

- [Tape-Image (.CDT) file format — cpctech](https://cpctech.cpcwiki.de/docs/cdt.html)
- [TZX format specification](https://k1.spdns.de/Develop/Projects/zasm/Info/TZX%20format.html)
- [Format:CDT tape image file format — CPCWiki](https://www.cpcwiki.eu/index.php/Format:CDT_tape_image_file_format)
- [8255 PPI on the CPC — cpctech](https://cpctech.cpcwiki.de/docs/8255cpc.html)
