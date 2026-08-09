using Adapters.Raylib;
using Machines.Sinclair.Common;
using Machines.ZxSpectrumPlus3;

// ── argument parsing ──────────────────────────────────────────────────────────
string?   romPath   = null;    // 64K combined image
string[]? romParts  = null;    // or four 16K images
string?   tapePath  = null;
string?   diskPath  = null;
bool      driveFitted = false;
bool      readOnly  = false;
int       scale     = 3;

var parts = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rom":   romPath  = args[++i]; break;
        case "--rom0":
        case "--rom1":
        case "--rom2":
        case "--rom3":
            parts.Add(args[i]);
            parts.Add(args[++i]);
            break;
        case "--tape":  tapePath = args[++i]; break;
        case "--disk":  diskPath = args[++i]; break;
        case "--drive": driveFitted = true; break;
        case "--read-only": readOnly = true; break;
        case "--scale": scale    = int.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

if (parts.Count > 0)
{
    // Order the four by their flag rather than by the order given, so a
    // mis-ordered command line is an error rather than a scrambled machine.
    var byIndex = new string?[4];
    for (int i = 0; i < parts.Count; i += 2)
    {
        int index = parts[i][^1] - '0';
        byIndex[index] = parts[i + 1];
    }

    if (byIndex.Any(p => p is null))
    {
        Console.Error.WriteLine(
            "All four ROMs are needed: --rom0 --rom1 --rom2 --rom3 (or one 64K image via --rom).");
        PrintUsage();
        return 1;
    }

    romParts = byIndex!;
}

// Asking for a disk without a drive to put it in is a mistake, not a request
// for a +2A that silently ignores the image.
if (diskPath is not null) driveFitted = true;

if (romPath is null && romParts is null)
{
    Console.Error.WriteLine("Supply either --rom <64K image> or --rom0..--rom3 <16K each>.");
    PrintUsage();
    return 1;
}

// ── build machine ─────────────────────────────────────────────────────────────
using var host = new RaylibHost("Sinclair ZX Spectrum +2A/+3", scale);

SinclairTapeAdapter? tape = null;
if (tapePath is not null)
{
    tape = new SinclairTapeAdapter();
    using var fs = File.OpenRead(tapePath);
    tape.Load(fs);
    Console.WriteLine($"Tape: {Path.GetFileName(tapePath)}");
}

Plus3Machine machine;

if (romParts is not null)
{
    var images = new byte[4][];
    for (int i = 0; i < 4; i++)
    {
        images[i] = File.ReadAllBytes(romParts[i]);
        Console.WriteLine($"ROM {i}: {Path.GetFileName(romParts[i])} ({images[i].Length} bytes)");
    }
    machine = new Plus3Machine(images, keyboard: host, audio: host, tape: tape, diskDrive: driveFitted);
}
else
{
    byte[] rom = File.ReadAllBytes(romPath!);
    Console.WriteLine($"ROM: {Path.GetFileName(romPath)} ({rom.Length} bytes)");
    machine = new Plus3Machine(rom, keyboard: host, audio: host, tape: tape, diskDrive: driveFitted);
}

machine.Reset();
Console.WriteLine($"PC after reset: ${machine.Cpu.PC:X4}   {machine.Ula.Timing.FrameCycles} T-states/frame");

if (machine.Fdc is null)
{
    Console.WriteLine("No disk drive fitted — this is a +2A. Pass --drive for a +3.");
}
else if (diskPath is not null)
{
    var disk = new DiskImage(File.ReadAllBytes(diskPath)) { IsWriteProtected = readOnly };
    machine.Fdc.InsertDisk(0, disk);

    Console.WriteLine(
        $"Drive A: {Path.GetFileName(diskPath)} — " +
        $"{disk.TrackCount} tracks, {disk.SideCount} side(s)" +
        (disk.IsExtended ? ", extended format" : "") +
        (readOnly ? ", write protected" : ""));

    // Writes go to the in-memory image and are not saved back, so saying so is
    // better than letting someone lose a saved game silently.
    if (!readOnly) Console.WriteLine("Writes are kept in memory only and are lost on exit.");
}

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
        Usage: zxplus3 (--rom <64K image> | --rom0..--rom3 <16K each>) [options]

        Options:
          --drive          Fit the floppy drive, making this a +3 rather than a +2A
          --disk  <path>   .DSK image for drive A (implies --drive)
          --read-only      Write protect the disk
          --tape  <path>   Tape image
          --scale <n>      Window scale factor (default: 3)

        The +2A/+3 has four 16K ROMs: 0 is the editor and menu, 1 the syntax
        checker, 2 is +3DOS and 3 is 48 BASIC. Supply them either as one 64K
        file or as four separate images.

        Without --drive the machine is a +2A, which is the same hardware minus
        the disk interface. Disk writes are kept in memory and lost on exit.
        """);
}
