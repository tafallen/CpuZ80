using Xunit;
using Machines.AmstradCpc;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// The MC6845 as a chip: register access rules, the cursor, the light pen and
/// interlace.
/// </summary>
/// <remarks>
/// The CPC leaves the cursor and light pen pins unconnected — it draws its own
/// cursor in software — so none of this changes what appears on screen. It is
/// the part's behaviour rather than the machine's, and programs detect which
/// CRTC is fitted by reading exactly these registers.
/// </remarks>
public class Mc6845Tests
{
    private const ushort Select = 0xBC00;
    private const ushort Write = 0xBD00;
    private const ushort Status = 0xBE00;
    private const ushort Read = 0xBF00;

    private static Mc6845 Crtc() => new();

    private static void Set(Mc6845 crtc, int register, byte value)
    {
        crtc.Out(Select, (byte)register);
        crtc.Out(Write, value);
    }

    private static byte Get(Mc6845 crtc, int register)
    {
        crtc.Out(Select, (byte)register);
        return crtc.In(Read);
    }

    // ── Which registers read back ────────────────────────────────────────────

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void CursorAndLightPenRegistersReadBack(int register)
    {
        var crtc = Crtc();

        // R16/R17 are read-only, so they are loaded through the light pen.
        if (register >= 16) crtc.StrobeLightPen(0x1234);
        else Set(crtc, register, 0x2A);

        Assert.NotEqual(0x00, Get(crtc, register));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(12)]   // read/write on the later *1 part, write-only here
    [InlineData(13)]
    public void EveryOtherRegisterIsWriteOnly(int register)
    {
        // Widening this would make the chip claim to be a UM6845R or an
        // MC6845*1, which is how software tells the CPC's CRTC types apart.
        var crtc = Crtc();

        Set(crtc, register, 0x2A);

        Assert.Equal(0x00, Get(crtc, register));
    }

    [Fact]
    public void TheLightPenRegistersAreReadOnly()
    {
        var crtc = Crtc();
        crtc.StrobeLightPen(0x0123);

        Set(crtc, 16, 0x3F);
        Set(crtc, 17, 0xFF);

        Assert.Equal(0x0123, crtc.LightPenAddress);
    }

    [Fact]
    public void ThereIsNoStatusRegister()
    {
        // A standard MC6845 has none; the parts that do are the UM6845R and the
        // ASICs. The CPC firmware reads VSync from the PPI instead.
        var crtc = Crtc();

        Assert.Equal(0x00, crtc.In(Status));
    }

    // ── Light pen ────────────────────────────────────────────────────────────

    [Fact]
    public void StrobingTheLightPenLatchesTheAddress()
    {
        var crtc = Crtc();

        crtc.StrobeLightPen(0x2ABC);

        Assert.Equal(0x2ABC, crtc.LightPenAddress);
        Assert.Equal(0x2A, Get(crtc, 16));
        Assert.Equal(0xBC, Get(crtc, 17));
    }

    [Fact]
    public void TheLightPenAddressIs14Bits()
    {
        var crtc = Crtc();

        crtc.StrobeLightPen(0xFFFF);

        Assert.Equal(0x3FFF, crtc.LightPenAddress);
    }

    // ── Cursor ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheCursorAddressComesFromR14AndR15()
    {
        var crtc = Crtc();

        Set(crtc, 14, 0x12);
        Set(crtc, 15, 0x34);

        Assert.Equal(0x1234, crtc.CursorAddress);
    }

    [Fact]
    public void TheCursorCoversTheScanlinesBetweenR10AndR11()
    {
        var crtc = Crtc();
        Set(crtc, 14, 0x00);
        Set(crtc, 15, 0x40);
        Set(crtc, 10, 2);        // start line 2, steady
        Set(crtc, 11, 5);        // end line 5

        Assert.False(crtc.IsCursorAt(0x0040, 1));
        Assert.True(crtc.IsCursorAt(0x0040, 2));
        Assert.True(crtc.IsCursorAt(0x0040, 5));
        Assert.False(crtc.IsCursorAt(0x0040, 6));
    }

    [Fact]
    public void TheCursorOnlyAppearsAtItsOwnAddress()
    {
        var crtc = Crtc();
        Set(crtc, 15, 0x40);
        Set(crtc, 10, 0);
        Set(crtc, 11, 7);

        Assert.True(crtc.IsCursorAt(0x0040, 0));
        Assert.False(crtc.IsCursorAt(0x0041, 0));
    }

    [Fact]
    public void BlinkMode1TurnsTheCursorOff()
    {
        var crtc = Crtc();
        Set(crtc, 15, 0x40);
        Set(crtc, 11, 7);
        Set(crtc, 10, 0x20);     // bits 6-5 = 01: disabled

        Assert.False(crtc.CursorBlinkOn);
        Assert.False(crtc.IsCursorAt(0x0040, 0));
    }

    [Fact]
    public void BlinkMode0IsSteady()
    {
        var crtc = Crtc();
        Set(crtc, 10, 0x00);

        for (int field = 0; field < 40; field++)
        {
            Assert.True(crtc.CursorBlinkOn);
            crtc.AdvanceField();
        }
    }

    [Theory]
    [InlineData(0x40, 16)]   // bits 6-5 = 10: every 16 fields
    [InlineData(0x60, 32)]   // bits 6-5 = 11: every 32 fields
    public void BlinkModesToggleAtTheirOwnRate(byte r10, int period)
    {
        var crtc = Crtc();
        Set(crtc, 10, r10);

        // Count how many fields pass before the state flips.
        bool initial = crtc.CursorBlinkOn;
        int fields = 0;
        while (crtc.CursorBlinkOn == initial && fields < 100)
        {
            crtc.AdvanceField();
            fields++;
        }

        Assert.Equal(period / 2, fields);
    }

    // ── Interlace ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x01, 1)]
    [InlineData(0x02, 2)]
    [InlineData(0x03, 3)]
    [InlineData(0xFF, 3)]   // only the low two bits are meaningful
    public void InterlaceModeIsTheLowTwoBitsOfR8(byte value, int mode)
    {
        var crtc = Crtc();

        Set(crtc, 8, value);

        Assert.Equal(mode, crtc.InterlaceMode);
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResetClearsTheCursorAndLightPen()
    {
        var crtc = Crtc();
        Set(crtc, 14, 0x12);
        Set(crtc, 15, 0x34);
        crtc.StrobeLightPen(0x2ABC);

        crtc.Reset();

        Assert.Equal(0, crtc.CursorAddress);
        Assert.Equal(0, crtc.LightPenAddress);
    }
}
