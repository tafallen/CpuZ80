using Adapters.Raylib;
using Machines.ZxSpectrum;
using Machines.Common;
using Machines.Sinclair.Common;

// ── argument parsing ──────────────────────────────────────────────────────────
string? romPath      = null;
string? snapshotPath = null;
string? tapePath     = null;
int     scale        = 3;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":      romPath      = args[++i]; break;
        case "--snapshot": snapshotPath = args[++i]; break;
        case "--tape":     tapePath     = args[++i]; break;
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

ZxSpectrumTapeAdapter? tape = null;
if (tapePath is not null)
{
    tape = new ZxSpectrumTapeAdapter();
    using var fs = File.OpenRead(tapePath);
    tape.Load(fs);
    Console.WriteLine($"Tape: {Path.GetFileName(tapePath)}");
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

return 0;

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: zxspec --rom <path> [options]

        Options:
          --snapshot <path>   ZX Spectrum snapshot image (.sna)
          --tape     <path>   ZX Spectrum tape image (.tap)
          --scale    <n>      Window scale factor (default: 3)
        """);
}
