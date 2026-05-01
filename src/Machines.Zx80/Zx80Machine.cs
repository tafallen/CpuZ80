using CpuZ80.Core;
using Machines.Common;

namespace Machines.Zx80;

/// <summary>
/// Sinclair ZX80 machine compositor.
///
/// Address map (A14-based partial decode — primary ranges only):
///   0x0000–0x0FFF  ROM  (4K — BASIC/OS ROM image)
///   0x4000–0x43FF  RAM  (1K — system variables + display file + BASIC program)
///   Everything else → 0xFF (unmapped)
///
/// Emulator loop:
///   machine.Reset();
///   while (running)
///   {
///       host.PollEvents();
///       machine.RunFrame();   // steps CPU for one frame worth of T-states
///   }
/// </summary>
public sealed class Zx80Machine
{
    private const int    RomSize        = 0x1000; // 4K
    private const int    RamSize        = 0x0400; // 1K
    private const ulong  CyclesPerFrame = 64167;  // 3,250,000 Hz ÷ 50 Hz

    private const uint   Ink   = 0xFF000000u; // black
    private const uint   Paper = 0xFFFFFFFFu; // white

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly byte[]      _romBytes; // held for RenderFrame font lookup
    private readonly Zx80PortBus _ports;

    /// <param name="rom">4K ROM image (ZX80 BASIC ROM). Must be exactly 4096 bytes.</param>
    /// <param name="keyboard">Physical keyboard source. Pass null for headless/test use.</param>
    public Zx80Machine(byte[] rom, IPhysicalKeyboard? keyboard = null)
    {
        if (rom.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {rom.Length}.", nameof(rom));

        _romBytes = (byte[])rom.Clone(); // keep a copy for font lookups in RenderFrame

        Ram = new Ram(RamSize);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x0FFF, new Rom(rom));
        bus.Map(0x4000, 0x43FF, Ram);

        var kbAdapter = keyboard is not null ? new Zx80KeyboardAdapter(keyboard) : null;
        _ports = new Zx80PortBus(kbAdapter);

        Cpu = new Cpu(bus, _ports);
    }

    /// <summary>Read a hardware port — delegates to the port bus. Useful for testing.</summary>
    public byte ReadPort(ushort port) => _ports.In(port);

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x0E;
    }

    public void Step() => Cpu.Step();

    public void RunFrame()
    {
        ulong target = Cpu.TotalCycles + CyclesPerFrame;
        while (Cpu.TotalCycles < target)
            Cpu.Step();
    }

    /// <summary>
    /// Render the current display file to <paramref name="sink"/> as a 256×192 ARGB32 frame.
    /// Reads D_FILE pointer from RAM 0x400C, walks the display file, looks up font bytes
    /// from the ROM copy held in _romBytes.
    /// </summary>
    public void RenderFrame(IVideoSink sink)
    {
        var pixels = new uint[256 * 192];
        var ram    = Ram.RawBytes;

        // D_FILE is a 16-bit little-endian pointer at RAM offset 0x000C (absolute 0x400C).
        ushort dfile = (ushort)(ram[0x000C] | (ram[0x000D] << 8));
        int pos = dfile - 0x4000; // convert to RAM array offset

        pos++; // skip the initial HALT byte

        for (int charRow = 0; charRow < 24; charRow++)
        {
            // Collect up to 32 char codes for this row, stopping at HALT (0x76).
            var rowCodes = new byte[32];
            int colCount = 0;
            while (pos < ram.Length && colCount < 32)
            {
                byte code = ram[pos++];
                if (code == 0x76) goto rowDone; // HALT = end of row
                rowCodes[colCount++] = code;
            }
            // Consume HALT if we hit the column limit without seeing one.
            while (pos < ram.Length && ram[pos] != 0x76) pos++;
            if (pos < ram.Length) pos++; // skip the HALT

            rowDone:
            // colCount chars; remaining cols are implicitly space (0x00)

            for (int col = 0; col < 32; col++)
            {
                byte code     = col < colCount ? rowCodes[col] : (byte)0x00;
                int  charBase = code & 0x3F;
                bool inverted = (code & 0x80) != 0;

                int fontOffset = 0x0E00 + charBase * 8;

                for (int pixRow = 0; pixRow < 8; pixRow++)
                {
                    byte fontByte = _romBytes[fontOffset + pixRow];
                    int  pixelY   = charRow * 8 + pixRow;

                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool set    = (fontByte & (0x80 >> bit)) != 0;
                        bool isInk  = set ^ inverted;
                        int  pixelX = col * 8 + bit;
                        pixels[pixelY * 256 + pixelX] = isInk ? Ink : Paper;
                    }
                }
            }
        }

        sink.SubmitFrame(pixels, 256, 192);
    }
}
