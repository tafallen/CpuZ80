using Adapters.Raylib;
using Machines.ZxSpectrum;
using Machines.Common;
using Machines.Sinclair.Common;

// ── argument parsing ──────────────────────────────────────────────────────────
string? romPath      = null;
string? snapshotPath = null;
string? tapePath     = null;
string? savePath     = null;
int     scale        = 3;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":      romPath      = args[++i]; break;
        case "--snapshot": snapshotPath = args[++i]; break;
        case "--tape":     tapePath     = args[++i]; break;
        case "--save-tape": savePath     = args[++i]; break;
        case "--scale":    scale        = int.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (romPath is null)
{
    Console.Error.WriteLine("--rom is required.");
    PrintUsage();
    return 1;
}

// ── load ROM ──────────────────────────────────────────────────────────────────
byte[] rom = File.ReadAllBytes(romPath);
Console.WriteLine($"ROM: {Path.GetFileName(romPath)} ({rom.Length} bytes)");

if (rom.Length != 0x4000)
    Console.Error.WriteLine($"WARNING: expected 16384 bytes, got {rom.Length}");

// ── build machine ─────────────────────────────────────────────────────────────
using var host = new RaylibHost("Sinclair ZX Spectrum 48K", scale);

// A recorder is fitted whenever either loading or saving is asked for: the
// same adapter does both, and SAVE with no deck attached is a silent no-op.
ZxSpectrumTapeAdapter? tape = null;
if (tapePath is not null || savePath is not null)
{
    tape = new ZxSpectrumTapeAdapter();

    if (tapePath is not null)
    {
        using var fs = File.OpenRead(tapePath);
        tape.Load(fs);
        Console.WriteLine($"Tape: {Path.GetFileName(tapePath)}");
    }

    if (savePath is not null) Console.WriteLine($"Saving to: {Path.GetFileName(savePath)} on exit");
}

var machine = new ZxSpectrumMachine(rom, keyboard: host, audio: host, tape: tape);
machine.Reset();

// ── load snapshot ─────────────────────────────────────────────────────────────
if (snapshotPath is not null)
{
    using var fs = File.OpenRead(snapshotPath);
    machine.LoadSnapshot(fs);
    Console.WriteLine($"Snapshot: {Path.GetFileName(snapshotPath)}");
}

Console.WriteLine($"PC: ${machine.Cpu.PC:X4}  I=${machine.Cpu.I:X2}");

// ── emulator loop ─────────────────────────────────────────────────────────────
while (host.IsRunning)
{
    host.PollEvents();
    machine.RunFrame();
    machine.RenderFrame(host);
}

if (savePath is not null && tape is not null)
{
    using var fs = File.Create(savePath);
    tape.Save(fs);

    int blocks = tape.RecordedBlocks.Count;
    Console.WriteLine(blocks == 0
        ? $"Nothing was saved, so {Path.GetFileName(savePath)} is empty."
        : $"Wrote {blocks} block(s) to {Path.GetFileName(savePath)}.");
}

return 0;

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: zxspec --rom <path> [options]

        Options:
          --snapshot <path>   ZX Spectrum snapshot image (.sna)
          --tape     <path>   ZX Spectrum tape image (.tap) to load from
          --save-tape <path>  Write anything SAVEd to this .tap on exit
          --scale    <n>      Window scale factor (default: 3)
        """);
}
