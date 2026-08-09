using Adapters.Raylib;
using Machines.AmstradCpc;

// ── argument parsing ──────────────────────────────────────────────────────────
string? romPath = null;    // combined OS + BASIC image
string? osPath = null;
string? basicPath = null;
string? amsdosPath = null;
bool has128K = true;
int scale = 2;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":    romPath    = args[++i]; break;
        case "--os":     osPath     = args[++i]; break;
        case "--basic":  basicPath  = args[++i]; break;
        case "--amsdos": amsdosPath = args[++i]; break;
        case "--464":    has128K    = false; break;
        case "--scale":  scale      = int.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (romPath is null && (osPath is null || basicPath is null))
{
    Console.Error.WriteLine("Supply either --rom <combined image> or --os <16K> --basic <16K>.");
    PrintUsage();
    return 1;
}

byte[] os, basic;

if (romPath is not null)
{
    // The images are usually distributed with a 128-byte AMSDOS header, and the
    // OS and BASIC halves in one file.
    byte[] image = AmsdosHeader.Strip(File.ReadAllBytes(romPath));

    if (image.Length != 0x8000)
    {
        Console.Error.WriteLine(
            $"A combined image is 32768 bytes (OS + BASIC), got {image.Length} after header stripping.");
        return 1;
    }

    os = image[..0x4000];
    basic = image[0x4000..];
    Console.WriteLine($"ROM: {Path.GetFileName(romPath)} (OS + BASIC)");
}
else
{
    os = File.ReadAllBytes(osPath!);
    basic = File.ReadAllBytes(basicPath!);
    Console.WriteLine($"OS: {Path.GetFileName(osPath)}   BASIC: {Path.GetFileName(basicPath)}");
}

using var host = new RaylibHost(
    has128K ? "Amstrad CPC 6128" : "Amstrad CPC 464", scale);

var machine = new CpcMachine(os, basic, keyboard: host, audio: host, has128K: has128K);

if (amsdosPath is not null)
{
    // AMSDOS lives at upper ROM 7.
    machine.Memory.AddUpperRom(7, File.ReadAllBytes(amsdosPath));
    Console.WriteLine($"AMSDOS: {Path.GetFileName(amsdosPath)} (upper ROM 7)");
}

machine.Reset();
Console.WriteLine($"PC after reset: ${machine.Cpu.PC:X4}   {CpcMachine.FrameCycles} T-states/frame");

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
        Usage: cpc (--rom <combined image> | --os <16K> --basic <16K>) [options]

        Options:
          --amsdos <path>  Disk ROM, fitted at upper ROM 7
          --464            A 464: 64K rather than 128K
          --scale <n>      Window scale factor (default: 2)

        A combined image holds the 16K OS followed by 16K of BASIC. A 128-byte
        AMSDOS header is detected and stripped, so the images as distributed
        work unmodified.
        """);
}
