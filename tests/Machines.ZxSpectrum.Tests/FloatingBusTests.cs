using Xunit;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// The ZX Spectrum "floating bus": reading an unattached port returns whatever
/// the ULA happens to be fetching at that instant.
/// </summary>
/// <remarks>
/// While drawing, the ULA fetches two bytes every 8 T-states — a bitmap byte
/// then an attribute byte. The attribute fetch is what a CPU read sees, so
/// within each 8 T-state group only sub-cycles 2, 3, 6 and 7 put a real value on
/// the bus. Everything else — the bitmap fetches, horizontal blanking, the
/// borders and vertical blanking — reads as 0xFF.
///
/// Games use this to synchronise with the raster without an interrupt.
///
/// Port 0x00FF is used throughout: A0 is high so the ULA does not answer, and A5
/// is high so the Kempston joystick does not either, leaving the open bus.
/// Cpu.TotalCycles is set directly to place the machine at a chosen T-state;
/// _frameStartCycles is 0 after Reset, so TotalCycles is the frame position.
/// </remarks>
public class FloatingBusTests
{
    private const int CyclesPerLine = 224;
    private const int VisibleStart = 64 * CyclesPerLine;  // 14,336
    private const int VisibleEnd = 256 * CyclesPerLine;   // 57,344
    private const ushort OpenBusPort = 0x00FF;

    /// <summary>Machine positioned at <paramref name="tState"/> with <paramref name="attr"/> seeded into attribute RAM.</summary>
    private static ZxSpectrumMachine MachineAt(ulong tState, ushort attrAddress = 0, byte attr = 0)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000]);
        machine.Reset();
        if (attrAddress != 0) machine.Ram.Write((ushort)(attrAddress - 0x4000), attr);
        machine.Cpu.TotalCycles = tState;
        return machine;
    }

    [Theory]
    // t = VisibleStart + offset. Line 64 starts exactly on a line boundary
    // (14,336 / 224 = 64), so the offset is also the position within the line.
    [InlineData(2, 0x5800)]  // sub-cycle 2, charX 0 -> first attribute of the row
    [InlineData(3, 0x5800)]  // sub-cycle 3, charX 0
    [InlineData(6, 0x5801)]  // sub-cycle 6, charX 1
    [InlineData(7, 0x5801)]  // sub-cycle 7, charX 1
    public void AttributeFetchCycles_ExposeTheAttributeByte(int offset, int attrAddress)
    {
        const byte Marker = 0xA5;
        var machine = MachineAt((ulong)(VisibleStart + offset), (ushort)attrAddress, Marker);

        Assert.Equal(Marker, machine.ReadPort(OpenBusPort));
    }

    [Theory]
    [InlineData(0)] // bitmap fetch
    [InlineData(1)] // bitmap fetch
    [InlineData(4)] // bitmap fetch
    [InlineData(5)] // bitmap fetch
    public void BitmapFetchCycles_ReadAsOpenBus(int offset)
    {
        // Seed every attribute cell so a wrong answer cannot coincidentally be 0xFF.
        var machine = MachineAt((ulong)(VisibleStart + offset));
        for (ushort i = 0x1800; i < 0x1B00; i++) machine.Ram.Write(i, 0xA5);

        Assert.Equal(0xFF, machine.ReadPort(OpenBusPort));
    }

    [Fact]
    public void SecondRowOfCharacters_SelectsTheNextAttributeRow()
    {
        // 8 scanlines down: charRow 1, so the attribute row moves on by 32 bytes.
        const byte Marker = 0x3C;
        ulong t = (ulong)(VisibleStart + (8 * CyclesPerLine) + 2);
        var machine = MachineAt(t, 0x5820, Marker);

        Assert.Equal(Marker, machine.ReadPort(OpenBusPort));
    }

    [Fact]
    public void BeforeVisibleArea_ReadsAsOpenBus()
    {
        var machine = MachineAt(0);
        for (ushort i = 0x1800; i < 0x1B00; i++) machine.Ram.Write(i, 0xA5);
        Assert.Equal(0xFF, machine.ReadPort(OpenBusPort));

        machine = MachineAt(VisibleStart - 1);
        for (ushort i = 0x1800; i < 0x1B00; i++) machine.Ram.Write(i, 0xA5);
        Assert.Equal(0xFF, machine.ReadPort(OpenBusPort));
    }

    [Fact]
    public void AfterVisibleArea_ReadsAsOpenBus()
    {
        var machine = MachineAt(VisibleEnd);
        for (ushort i = 0x1800; i < 0x1B00; i++) machine.Ram.Write(i, 0xA5);
        Assert.Equal(0xFF, machine.ReadPort(OpenBusPort));
    }

    [Fact]
    public void DuringHorizontalBlanking_ReadsAsOpenBus()
    {
        // Only the first 128 T-states of a line are drawn.
        for (int offset = 128; offset < CyclesPerLine; offset += 8)
        {
            var machine = MachineAt((ulong)(VisibleStart + offset));
            for (ushort i = 0x1800; i < 0x1B00; i++) machine.Ram.Write(i, 0xA5);

            Assert.Equal(0xFF, machine.ReadPort(OpenBusPort));
        }
    }

    [Fact]
    public void FloatingBus_TracksTheCurrentTState_NotTheLastMemoryAccess()
    {
        // The value must be sampled when the port is read. Two reads at
        // different T-states over different attribute cells must differ.
        const byte First = 0x11;
        const byte Second = 0x22;

        var machine = new ZxSpectrumMachine(new byte[0x4000]);
        machine.Reset();
        machine.Ram.Write(0x5800 - 0x4000, First);
        machine.Ram.Write(0x5801 - 0x4000, Second);

        machine.Cpu.TotalCycles = (ulong)(VisibleStart + 2); // charX 0
        Assert.Equal(First, machine.ReadPort(OpenBusPort));

        machine.Cpu.TotalCycles = (ulong)(VisibleStart + 6); // charX 1
        Assert.Equal(Second, machine.ReadPort(OpenBusPort));
    }

    [Fact]
    public void UlaPort_IsUnaffectedByTheFloatingBus()
    {
        // Port 0xFE (A0 low) is the ULA's own; it must answer with keyboard
        // state, not the floating bus, even mid-attribute-fetch.
        var machine = MachineAt((ulong)(VisibleStart + 2), 0x5800, 0xA5);

        byte result = machine.ReadPort(0xFEFE);

        Assert.NotEqual(0xA5, result);
    }
}
