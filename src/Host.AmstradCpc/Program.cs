using Adapters.Raylib;
using Machines.AmstradCpc;

// ── argument parsing ──────────────────────────────────────────────────────────
string? romPath = null;    // combined OS + BASIC image
string? osPath = null;
string? basicPath = null;
string? amsdosPath = null;
string? tapePath = null;
CpcModel model = CpcModel.Cpc6128;
int scale = 2;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":    romPath    = args[++i]; break;
        case "--os":     osPath     = args[++i]; break;
        case "--basic":  basicPath  = args[++i]; break;
        case "--amsdos": amsdosPath = args[++i]; break;
        case "--tape":   tapePath   = args[++i]; break;
        case "--464":    model      = CpcModel.Cpc464; break;
        case "--664":    model      = CpcModel.Cpc664; break;
        case "--6128":   model      = CpcModel.Cpc6128; break;
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

using var host = new RaylibHost(model.DisplayName(), scale);

CdtTape? tape = null;
if (tapePath is not null)
{
    tape = new CdtTape(CpcMachine.ClockHz);
    using var fs = File.OpenRead(tapePath);
    tape.Load(fs);

    Console.WriteLine(
        $"Tape: {Path.GetFileName(tapePath)} — {tape.DataBlockCount} block(s), " +
        $"{tape.LengthInTStates / (ulong)CpcMachine.ClockHz}s");

    foreach (string description in tape.Descriptions) Console.WriteLine($"  {description}");

    if (!model.HasCassette())
    {
        Console.WriteLine(
            $"Note: a {model.DisplayName()} has no cassette deck. The tape is attached anyway, " +
            "as an external recorder would be.");
    }
}

var machine = new CpcMachine(model, os, basic, keyboard: host, audio: host, tape: tape);

if (amsdosPath is not null)
{
    // AMSDOS lives at upper ROM 7.
    machine.Memory.AddUpperRom(7, File.ReadAllBytes(amsdosPath));
    Console.WriteLine($"AMSDOS: {Path.GetFileName(amsdosPath)} (upper ROM 7)");
}
else if (model.HasDiskDrive())
{
    Console.WriteLine("No AMSDOS ROM supplied, so the drive is fitted but the disk commands are absent.");
}

Console.WriteLine(
    $"{model.DisplayName()}: {(model.Has128K() ? 128 : 64)}K, " +
    $"{(model.HasDiskDrive() ? "3\" disk" : "cassette")}");

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
          --tape   <path>  .CDT tape image to load from
          --464            Amstrad CPC 464: 64K, cassette, BASIC 1.0
          --664            Amstrad CPC 664: 64K, 3" disk, BASIC 1.1
          --6128           Amstrad CPC 6128: 128K, 3" disk (the default)
          --scale <n>      Window scale factor (default: 2)

        A combined image holds the 16K OS followed by 16K of BASIC. A 128-byte
        AMSDOS header is detected and stripped, so the images as distributed
        work unmodified.

        Each model needs its own ROMs: the 464 has BASIC 1.0, the 664 and 6128
        BASIC 1.1. Passing one model's ROMs with another model's flag will boot
        something, but it will not be that machine.

        With a tape attached, type RUN" and press ENTER, then start the tape.
        """);
}
