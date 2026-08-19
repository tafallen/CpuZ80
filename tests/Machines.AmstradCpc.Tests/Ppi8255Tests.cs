using Xunit;
using Machines.AmstradCpc;
using Machines.ZxSpectrum128;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// The Intel 8255 as a chip: directions, mode selection and the mode 1 and 2
/// handshake lines.
/// </summary>
/// <remarks>
/// The CPC uses mode 0 throughout and connects nothing to the handshake lines,
/// so the modes here are the part behaving correctly rather than the machine
/// doing anything new. Directions, though, matter on a CPC: the firmware turns
/// port A round between writing to the PSG and reading the keyboard back.
/// </remarks>
public class Ppi8255Tests
{
    private const ushort PortA = 0xF400;
    private const ushort PortB = 0xF500;
    private const ushort PortC = 0xF600;
    private const ushort Control = 0xF700;

    private static Ppi8255 Ppi() => new(new Ay38912());

    // ── Directions ───────────────────────────────────────────────────────────

    [Fact]
    public void AnOutputPortReadsBackItsOwnLatch()
    {
        // Not the outside world: an output is driving the pins, so that is what
        // is on them.
        var ppi = Ppi();

        ppi.Out(Control, 0x80);          // everything an output
        ppi.Out(PortA, 0x5A);

        Assert.False(ppi.PortAIsInput);
        Assert.Equal(0x5A, ppi.In(PortA));
    }

    [Fact]
    public void PortBAsAnOutputStopsReportingVSync()
    {
        // Port B is an input on a CPC, which is what makes the VSync bit
        // readable. Configured the other way it reads its latch instead.
        var ppi = Ppi();
        ppi.VSync = true;

        ppi.Out(Control, 0x82);          // B input
        Assert.Equal(1, ppi.In(PortB) & 0x01);

        ppi.Out(Control, 0x80);          // B output
        ppi.Out(PortB, 0x00);
        Assert.Equal(0, ppi.In(PortB) & 0x01);
    }

    [Fact]
    public void PortCsTwoHalvesHaveIndependentDirections()
    {
        var ppi = Ppi();
        ppi.PortCInput = 0xA5;

        ppi.Out(Control, 0x81);          // lower input, upper output
        ppi.Out(PortC, 0xF0);

        Assert.True(ppi.PortCLowerIsInput);
        Assert.False(ppi.PortCUpperIsInput);

        // Upper nibble from the latch, lower from outside.
        Assert.Equal(0xF5, ppi.In(PortC));
    }

    [Fact]
    public void TheControlWordReadsBack()
    {
        var ppi = Ppi();

        ppi.Out(Control, 0x92);

        Assert.Equal(0x92, ppi.In(Control));
    }

    [Fact]
    public void AModeSetResetsTheOutputLatches()
    {
        // The datasheet is explicit that a mode-set word clears the output
        // registers, which is why the CPC firmware writes port C *after*
        // turning port A round rather than before.
        var ppi = Ppi();

        ppi.Out(Control, 0x80);
        ppi.Out(PortA, 0xFF);
        ppi.Out(PortC, 0xFF);

        ppi.Out(Control, 0x80);          // same mode, written again

        Assert.Equal(0x00, ppi.In(PortA));
        Assert.Equal(0x00, ppi.In(PortC));
    }

    [Fact]
    public void BitSetResetTouchesOnlyOnePortCBit()
    {
        var ppi = Ppi();
        ppi.Out(Control, 0x80);

        ppi.Out(Control, 0x0B);          // set bit 5
        Assert.Equal(0x20, ppi.In(PortC));

        ppi.Out(Control, 0x0A);          // reset bit 5
        Assert.Equal(0x00, ppi.In(PortC));
    }

    // ── Mode selection ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x80, 0, 0)]
    [InlineData(0xA0, 1, 0)]
    [InlineData(0xC0, 2, 0)]
    [InlineData(0xE0, 2, 0)]   // bits 6-5 = 1x is mode 2 either way
    [InlineData(0x84, 0, 1)]
    public void TheControlWordSelectsEachGroupsMode(byte control, int groupA, int groupB)
    {
        var ppi = Ppi();

        ppi.Out(Control, control);

        Assert.Equal(groupA, ppi.GroupAMode);
        Assert.Equal(groupB, ppi.GroupBMode);
    }

    // ── Mode 1 handshaking ───────────────────────────────────────────────────

    [Fact]
    public void Mode1Input_StrobeSetsIbfAndInterrupts()
    {
        // PC5 is IBF and PC3 is INTR for group A input.
        var ppi = Ppi();
        ppi.Out(Control, 0xB0);          // group A mode 1, port A input

        Assert.False(ppi.InterruptRequested);

        ppi.StrobePortA(0x42);

        Assert.True(ppi.InterruptRequested);
        Assert.Equal(0x20, ppi.In(PortC) & 0x20);   // IBF
        Assert.Equal(0x08, ppi.In(PortC) & 0x08);   // INTR
    }

    [Fact]
    public void Mode1Input_ReadingThePortClearsIbfAndTheInterrupt()
    {
        var ppi = Ppi();
        ppi.Out(Control, 0xB0);
        ppi.StrobePortA(0x42);

        Assert.Equal(0x42, ppi.In(PortA));

        Assert.False(ppi.InterruptRequested);
        Assert.Equal(0x00, ppi.In(PortC) & 0x20);
    }

    [Fact]
    public void Mode1Output_WritingSetsObfAndAcknowledgingClearsIt()
    {
        // PC7 is OBF and PC6 is ACK for group A output.
        var ppi = Ppi();
        ppi.Out(Control, 0xA0);          // group A mode 1, port A output

        ppi.Out(PortA, 0x37);
        Assert.Equal(0x80, ppi.In(PortC) & 0x80);   // OBF: data waiting

        ppi.AcknowledgePortA();
        Assert.Equal(0x00, ppi.In(PortC) & 0x80);
        Assert.True(ppi.InterruptRequested);        // ready for the next byte
    }

    [Fact]
    public void GroupBHasItsOwnHandshakeLines()
    {
        // PC1 is IBF and PC0 is INTR for group B.
        var ppi = Ppi();
        ppi.Out(Control, 0x86);          // group B mode 1, port B input

        ppi.StrobePortB(0x99);

        Assert.Equal(0x02, ppi.In(PortC) & 0x02);
        Assert.Equal(0x01, ppi.In(PortC) & 0x01);
        Assert.Equal(0x99, ppi.In(PortB));
        Assert.Equal(0x00, ppi.In(PortC) & 0x02);
    }

    [Fact]
    public void HandshakeLinesOverrideThePortCLatch()
    {
        // The chip drives those bits itself, so a write to port C cannot set
        // them.
        var ppi = Ppi();
        ppi.Out(Control, 0xB0);          // group A mode 1 input

        ppi.Out(PortC, 0xFF);

        Assert.Equal(0x00, ppi.In(PortC) & 0x20);   // IBF is the chip's to drive
    }

    [Fact]
    public void InMode0TheHandshakeCallsDoNothing()
    {
        // Nothing on a CPC drives them, and a stray strobe must not invent an
        // interrupt.
        var ppi = Ppi();
        ppi.Out(Control, 0x82);          // mode 0 throughout

        ppi.StrobePortA(0x42);
        ppi.StrobePortB(0x42);
        ppi.AcknowledgePortA();

        Assert.False(ppi.InterruptRequested);
    }

    // ── Mode 2 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Mode2UsesBothDirectionsOnPortA()
    {
        var ppi = Ppi();
        ppi.Out(Control, 0xC0);          // group A mode 2

        Assert.Equal(2, ppi.GroupAMode);

        ppi.Out(PortA, 0x11);
        Assert.Equal(0x80, ppi.In(PortC) & 0x80);   // OBF, output side

        ppi.StrobePortA(0x22);
        Assert.Equal(0x20, ppi.In(PortC) & 0x20);   // IBF, input side
    }

    // ── Decoding ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnlyAnswersWhenA11IsClear()
    {
        var ppi = Ppi();
        ppi.Out(Control, 0x80);
        ppi.Out(PortA, 0x5A);

        Assert.Equal(0x5A, ppi.In(0xF400));
        Assert.Equal(0xFF, ppi.In(0xFC00));   // A11 set: not ours
    }
}
