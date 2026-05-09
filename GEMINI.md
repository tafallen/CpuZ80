# CpuZ80 Project Instructions

This project emulates the Zilog Z80 CPU with a focus on functional accuracy and composition-based machine building.

## Architecture

- **Modular CPU:** The `Cpu` class is a `sealed partial` class. Functional logic is grouped by type (Arithmetic, Bitwise, etc.), while instruction dispatch is handled by `Cpu.Generated.cs`.
- **Bus Abstraction:** The CPU interacts with memory via `IBus` and I/O via `IPortBus`.
- **High-Performance Dispatch:** Uses a generated `switch` dispatcher with inlined T-state interleaving for maximum performance and timing accuracy.
- **Silicon-Accurate Timing:** Interleaves `Tick(n)` calls with memory and I/O operations at the M-cycle level, enabling accurate hardware contention emulation.
- **Explicit Prefix Tables:** `DD` (IX) and `FD` (IY) prefixes use dedicated, generated dispatch tables, eliminating fragile runtime redirection flags.

## Engineering Standards

- **TDD Requirement:** All new features or bug fixes must include unit tests.
- **Verification:** Tests must assert both functional state (registers, flags, memory) and precise `TotalCycles`.
- **Coverage:** Aim for >85% branch coverage.
- **Instruction Accuracy:** Documented instructions must pass the ZEXALL instruction exerciser.

## Workflow

- **Documentation First:** Major architectural changes or new machine implementations should start with a deep-dive research phase and an architectural critique.
- **Analysis Storage:** Detailed technical analyses and critiques should be saved to the `docs/` directory as markdown files.
- **Milestones:** Follow the Epic-based milestones defined in `docs/backlog.md`.

## Key Interfaces

- `IBus`: Memory read/write.
- `IPortBus`: I/O port `In`/`Out`.
- `AddressDecoder`: Routes traffic using a last-registration-wins mapping strategy.

---
## Code Search

Use `semble search` to find code by describing what it does or naming a symbol/identifier, instead of grep:

​```bash
semble search "authentication flow" ./my-project
semble search "save_pretrained" ./my-project
semble search "save model to disk" ./my-project --top-k 10
​```

Use `semble find-related` to discover code similar to a known location (pass `file_path` and `line` from a prior search result):

​```bash
semble find-related src/auth.py 42 ./my-project
​```

`path` defaults to the current directory when omitted; git URLs are accepted.

If `semble` is not on `$PATH`, use `uvx --from "semble[mcp]" semble` in its place.

## Workflow

1. Start with `semble search` to find relevant chunks.
2. Inspect full files only when the returned chunk is not enough context.
3. Optionally use `semble find-related` with a promising result's `file_path` and `line` to discover related implementations.
4. Use grep only when you need exhaustive literal matches or quick confirmation of an exact string.


---
Look in Claude.md for more context, instructions and configuration.