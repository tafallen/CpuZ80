using Adapters.Raylib;
using Machines.Common;
using Machines.Sinclair.Common;
using Machines.ZxSpectrum128;

// ── argument parsing ──────────────────────────────────────────────────────────
string? romPath  = null;   // 32K combined image
string? rom0Path = null;   // or two 16K images
string? rom1Path = null;
string? tapePath = null;
int     scale    = 3;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":   romPath  = args[++i]; break;
        case "--rom0":  rom0Path = args[++i]; break;
        case "--rom1":  rom1Path = args[++i]; break;
        case "--tape":  tapePath = args[++i]; break;
        case "--scale": scale    = int.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (romPath is null && rom0Path is null)
{
    Console.Error.WriteLine("Supply either --rom <32K image> or --rom0 <16K> --rom1 <16K>.");
    PrintUsage();
    return 1;
}

if (rom0Path is not null && rom1Path is null)
{
    Console.Error.WriteLine("--rom0 given without --rom1. The 128 has two ROMs: 128-0.rom and 128-1.rom.");
    PrintUsage();
    return 1;
}

// ── build machine ─────────────────────────────────────────────────────────────
using var host = new RaylibHost("Sinclair ZX Spectrum 128", scale);

SinclairTapeAdapter? tape = null;
if (tapePath is not null)
{
    tape = new SinclairTapeAdapter();
    using var fs = File.OpenRead(tapePath);
    tape.Load(fs);
    Console.WriteLine($"Tape: {Path.GetFileName(tapePath)}");
}

Zx128Machine machine;

if (rom0Path is not null)
{
    byte[] rom0 = File.ReadAllBytes(rom0Path);
    byte[] rom1 = File.ReadAllBytes(rom1Path!);
    Console.WriteLine($"ROM 0: {Path.GetFileName(rom0Path)} ({rom0.Length} bytes)");
    Console.WriteLine($"ROM 1: {Path.GetFileName(rom1Path)} ({rom1.Length} bytes)");
    machine = new Zx128Machine(rom0, rom1, keyboard: host, audio: host, tape: tape);
}
else
{
    byte[] rom = File.ReadAllBytes(romPath!);
    Console.WriteLine($"ROM: {Path.GetFileName(romPath)} ({rom.Length} bytes)");
    machine = new Zx128Machine(rom, keyboard: host, audio: host, tape: tape);
}

if (!machine.Rom1Present)
{
    Console.Error.WriteLine(
        "WARNING: only ROM 0 was supplied. Selecting 48 BASIC will page in an empty ROM.");
}

machine.Reset();
Console.WriteLine($"PC after reset: ${machine.Cpu.PC:X4}   {machine.Ula.Timing.FrameCycles} T-states/frame");

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
        Usage: zx128 (--rom <32K image> | --rom0 <16K> --rom1 <16K>) [options]

        Options:
          --tape  <path>   Tape image
          --scale <n>      Window scale factor (default: 3)

        The ZX Spectrum 128 has two 16K ROMs: ROM 0 is the 128 editor and
        ROM 1 is 48 BASIC. Supply them either as one 32K file or as the two
        files they are usually distributed as (128-0.rom, 128-1.rom).
        """);
}
