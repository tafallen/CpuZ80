using Adapters.Raylib;
using Machines.Zx80;

// ── argument parsing ──────────────────────────────────────────────────────────
string? romPath  = null;
string? tapePath = null;
int     scale    = 3;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":   romPath  = args[++i]; break;
        case "--tape":  tapePath = args[++i]; break;
        case "--scale": scale    = int.Parse(args[++i]); break;
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

if (rom.Length != 0x1000)
    Console.Error.WriteLine($"WARNING: expected 4096 bytes, got {rom.Length}");

// ── load tape ─────────────────────────────────────────────────────────────────
Zx80TapeAdapter? tape = null;
if (tapePath is not null)
{
    tape = new Zx80TapeAdapter();
    using var fs = File.OpenRead(tapePath);
    tape.Load(fs);
    Console.WriteLine($"Tape: {Path.GetFileName(tapePath)}");
}

// ── build machine ─────────────────────────────────────────────────────────────
using var host = new RaylibHost("Sinclair ZX80", scale);

var machine = new Zx80Machine(rom, keyboard: host, tape: tape);
machine.Reset();
Console.WriteLine($"PC after reset: ${machine.Cpu.PC:X4}  I=${machine.Cpu.I:X2}");

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
        Usage: zx80 --rom <path> [options]

        Options:
          --tape  <path>   ZX80 tape image (.o or .80)
          --scale <n>      Window scale factor (default: 3)
        """);
}
