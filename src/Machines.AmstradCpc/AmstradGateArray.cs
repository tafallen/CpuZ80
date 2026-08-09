using CpuZ80.Core;

namespace Machines.AmstradCpc;

/// <summary>
/// The Amstrad Gate Array: palette, screen mode, ROM paging, RAM banking and
/// the raster interrupt counter.
/// </summary>
/// <remarks>
/// Selected for I/O writes when A15 = 0 and A14 = 1, conventionally &amp;7Fxx.
/// Write-only — there is nothing to read back.
///
/// The top two bits of the data byte pick the register:
/// <c>00</c> PENR, <c>01</c> INKR, <c>10</c> RMR, <c>11</c> MMR. Two published
/// references disagree about this; see docs/amstrad-cpc.md §2.1.
/// </remarks>
public sealed class AmstradGateArray : IPortBus
{
    public const int PenCount = 16;
    private const int BorderPen = 16;   // the border is a 17th palette entry

    private readonly CpcMemory _memory;

    /// <summary>Selected pen, or <see cref="BorderPen"/> for the border.</summary>
    private int _selectedPen;

    /// <summary>Hardware colour per pen, plus the border at index 16.</summary>
    private readonly byte[] _pens = new byte[PenCount + 1];

    /// <summary>Screen mode 0-3.</summary>
    public int ScreenMode { get; private set; }

    /// <summary>
    /// The interrupt counter, incremented on each HSync and reset at 52.
    /// </summary>
    public int RasterCounter { get; private set; }

    /// <summary>Raised when the counter reaches 52 and the Gate Array asserts INT.</summary>
    public event Action? InterruptRequested;

    public AmstradGateArray(CpcMemory memory) => _memory = memory;

    public void Reset()
    {
        _selectedPen = 0;
        Array.Clear(_pens);
        ScreenMode = 1;
        RasterCounter = 0;
    }

    /// <summary>The hardware colour currently assigned to the border.</summary>
    public byte BorderColour => _pens[BorderPen];

    /// <summary>The hardware colour for a pen, 0-15.</summary>
    public byte InkFor(int pen) => _pens[pen & 0x0F];

    // ── Ports ────────────────────────────────────────────────────────────────

    /// <summary>Gate Array: A15 clear, A14 set.</summary>
    private static bool IsGateArrayPort(ushort port) => (port & 0xC000) == 0x4000;

    public byte In(ushort port) => 0xFF;   // write-only

    public void Out(ushort port, byte value)
    {
        if (!IsGateArrayPort(port)) return;

        switch (value & 0xC0)
        {
            case 0x00: SelectPen(value); break;
            case 0x40: AssignInk(value); break;
            case 0x80: WriteRmr(value); break;
            case 0xC0: WriteMmr(value); break;
        }
    }

    private void SelectPen(byte value)
    {
        // Bit 4 selects the border instead of one of the sixteen pens.
        _selectedPen = (value & 0x10) != 0 ? BorderPen : value & 0x0F;
    }

    private void AssignInk(byte value) => _pens[_selectedPen] = (byte)(value & 0x1F);

    private void WriteRmr(byte value)
    {
        ScreenMode = value & 0x03;

        // The ROM enables are ACTIVE LOW: a set bit disables. Inverting this
        // maps RAM where the OS expects ROM and the machine dies immediately.
        bool lowerEnabled = (value & 0x04) == 0;
        bool upperEnabled = (value & 0x08) == 0;
        _memory.SetRomEnables(lowerEnabled, upperEnabled);

        // Bit 4 clears the interrupt counter, which software uses to
        // synchronise raster effects.
        if ((value & 0x10) != 0) RasterCounter = 0;
    }

    private void WriteMmr(byte value) => _memory.SetRamConfig(value & 0x07);

    // ── Raster interrupts ────────────────────────────────────────────────────

    /// <summary>
    /// Called on every HSync falling edge. At 52 the Gate Array raises INT and
    /// resets the counter, which is 300 Hz on a PAL machine — six interrupts per
    /// 50 Hz frame, not one.
    /// </summary>
    public void OnHSync()
    {
        RasterCounter++;
        if (RasterCounter < 52) return;

        RasterCounter = 0;
        InterruptRequested?.Invoke();
    }

    /// <summary>
    /// Called when the CPU acknowledges an interrupt: bit 5 of the counter is
    /// cleared, which prevents a second interrupt within 32 HSyncs.
    /// </summary>
    public void OnInterruptAcknowledged() => RasterCounter &= ~0x20;
}
