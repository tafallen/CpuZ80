using Xunit;
using Machines.AmstradCpc;
using Machines.Common;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// The keyboard path: CPU → PPI → PSG → matrix.
/// </summary>
/// <remarks>
/// Three components have to be right before a single keypress registers, and a
/// failure in any of them looks identical from outside — nothing happens. The
/// matrix table was written from a reference without a key ever being pressed,
/// so these tests exist to close that gap rather than to guard a known-good
/// implementation.
/// </remarks>
public class KeyboardTests
{
    private sealed class FakeKeyboard : IPhysicalKeyboard
    {
        private readonly HashSet<PhysicalKey> _down = [];

        public void Press(PhysicalKey key) => _down.Add(key);
        public void Release(PhysicalKey key) => _down.Remove(key);
        public void ReleaseAll() => _down.Clear();

        public bool IsKeyDown(PhysicalKey key) => _down.Contains(key);
    }

    // ── The matrix itself ────────────────────────────────────────────────────

    [Fact]
    public void NothingPressed_EveryRowReadsAllOnes()
    {
        // Active low: a clear bit means pressed.
        var matrix = new CpcKeyboard(new FakeKeyboard());

        for (int row = 0; row < 10; row++)
        {
            Assert.Equal(0xFF, matrix.ReadRow(row));
        }
    }

    [Theory]
    // Line, then bit — from the published matrix, not from the implementation.
    [InlineData(PhysicalKey.Space, 5, 7)]
    [InlineData(PhysicalKey.A, 8, 5)]
    [InlineData(PhysicalKey.Z, 8, 7)]
    [InlineData(PhysicalKey.D0, 4, 0)]
    [InlineData(PhysicalKey.D1, 8, 0)]
    [InlineData(PhysicalKey.LeftShift, 2, 5)]
    [InlineData(PhysicalKey.LeftControl, 2, 7)]
    [InlineData(PhysicalKey.Escape, 8, 2)]
    [InlineData(PhysicalKey.Up, 0, 0)]
    [InlineData(PhysicalKey.V, 6, 7)]
    public void APressedKeyClearsExactlyOneBit(PhysicalKey key, int line, int bit)
    {
        var host = new FakeKeyboard();
        var matrix = new CpcKeyboard(host);

        host.Press(key);

        Assert.Equal((byte)(0xFF & ~(1 << bit)), matrix.ReadRow(line));

        // And no other line is disturbed.
        for (int l = 0; l < 10; l++)
        {
            if (l != line) Assert.Equal(0xFF, matrix.ReadRow(l));
        }
    }

    [Fact]
    public void ReturnAppearsOnLineTwo()
    {
        // Return is on line 2 bit 2. Line 0 bit 6 is the numeric keypad's Enter,
        // which is a different key on real hardware.
        var host = new FakeKeyboard();
        var matrix = new CpcKeyboard(host);

        host.Press(PhysicalKey.Return);

        Assert.Equal(0xFF & ~(1 << 2), matrix.ReadRow(2));
    }

    [Fact]
    public void ReadingAnOutOfRangeRowIsSafe()
    {
        var matrix = new CpcKeyboard(new FakeKeyboard());

        Assert.Equal(0xFF, matrix.ReadRow(-1));
        Assert.Equal(0xFF, matrix.ReadRow(99));
    }

    // ── Through the PPI and PSG ──────────────────────────────────────────────

    /// <summary>
    /// Reads a keyboard row the way the firmware does: select PSG register 14,
    /// point port C at the row, then read PPI port A with the PSG in read mode.
    /// </summary>
    private static byte ReadRowThroughHardware(CpcMachine machine, int row)
    {
        const ushort PsgControl = 0xF600;   // PPI port C
        const ushort PsgData = 0xF400;      // PPI port A

        // Put register 14 (the keyboard port) on the PSG's data bus and latch it.
        machine.WritePort(PsgData, 14);
        machine.WritePort(PsgControl, 0xC0);   // BDIR=1 BC1=1: select register
        machine.WritePort(PsgControl, 0x00);   // back to inactive

        // Select the row in port C's low nibble, and put the PSG into read mode.
        machine.WritePort(PsgControl, (byte)(0x40 | row));

        return machine.ReadPort(PsgData);
    }

    [Fact]
    public void AKeyPressIsVisibleThroughThePpiAndPsg()
    {
        // The end-to-end path, without the firmware in the way.
        var host = new FakeKeyboard();
        var machine = new CpcMachine(CpcBootTests.TestRom(), CpcBootTests.TestRom(), keyboard: host);
        machine.Reset();

        Assert.Equal(0xFF, ReadRowThroughHardware(machine, 5));

        host.Press(PhysicalKey.Space);

        Assert.Equal(0xFF & ~(1 << 7), ReadRowThroughHardware(machine, 5));
    }

    [Fact]
    public void TheSelectedRowComesFromPortCsLowNibble()
    {
        var host = new FakeKeyboard();
        var machine = new CpcMachine(CpcBootTests.TestRom(), CpcBootTests.TestRom(), keyboard: host);
        machine.Reset();

        host.Press(PhysicalKey.A);   // row 8, bit 5

        Assert.Equal(0xFF, ReadRowThroughHardware(machine, 0));
        Assert.Equal(0xFF & ~(1 << 5), ReadRowThroughHardware(machine, 8));
    }

    // ── End to end, with the firmware ────────────────────────────────────────

    [Fact]
    public void TypingAtTheBasicPrompt_EchoesToTheScreen()
    {
        // The real proof: the firmware scans the matrix itself, decodes the key
        // and echoes it. Nothing below the firmware can be faked here.
        //
        // Run two identical machines for identical frame counts, one of which
        // sees a keypress. Comparing against a baseline rather than against
        // "did anything change" matters because the cursor blinks at the Ready
        // prompt — a bare "the screen changed" assertion passes with the
        // keyboard entirely disconnected.
        if (BuildRealMachine(out var typing, out var idle) is false) return;

        for (int i = 0; i < 150; i++) { typing.RunFrame(); idle.RunFrame(); }

        Assert.Equal(ScreenOf(idle), ScreenOf(typing));   // identical so far

        // Hold the key for several frames: the firmware debounces, so a single
        // frame is not enough.
        typing.PressKey(PhysicalKey.A);
        for (int i = 0; i < 12; i++) { typing.RunFrame(); idle.RunFrame(); }
        typing.ReleaseKeys();
        for (int i = 0; i < 12; i++) { typing.RunFrame(); idle.RunFrame(); }

        Assert.NotEqual(ScreenOf(idle), ScreenOf(typing));
    }

    [Fact]
    public void TypingABasicCommand_RunsItAndPrintsTheResult()
    {
        // The strongest end-to-end check available without a font decoder:
        // type PRINT 123 and press Return. BASIC echoes the command, executes
        // it, prints the result and a fresh Ready — three more lines of text
        // than an idle machine accumulates from its blinking cursor.
        if (BuildRealMachine(out var typing, out var idle) is false) return;

        for (int i = 0; i < 150; i++) { typing.RunFrame(); idle.RunFrame(); }

        PhysicalKey[] command =
        [
            PhysicalKey.P, PhysicalKey.R, PhysicalKey.I, PhysicalKey.N, PhysicalKey.T,
            PhysicalKey.Space, PhysicalKey.D1, PhysicalKey.D2, PhysicalKey.D3,
            PhysicalKey.Return,
        ];

        foreach (var key in command)
        {
            typing.PressKey(key);
            for (int i = 0; i < 8; i++) { typing.RunFrame(); idle.RunFrame(); }
            typing.ReleaseKeys();
            for (int i = 0; i < 8; i++) { typing.RunFrame(); idle.RunFrame(); }
        }

        for (int i = 0; i < 30; i++) { typing.RunFrame(); idle.RunFrame(); }

        int typed = ScreenOf(typing).Count(b => b != 0);
        int untouched = ScreenOf(idle).Count(b => b != 0);

        Assert.True(typed > untouched + 100,
            $"running PRINT 123 should add several lines of text, but the typing machine " +
            $"has {typed} set bytes against an idle machine's {untouched}");
    }

    private static byte[] ScreenOf(TestMachine machine)
    {
        var bank = machine.Machine.Memory.Banks[3];
        byte[] screen = new byte[0x4000];
        for (int a = 0; a < screen.Length; a++) screen[a] = bank.Read((ushort)a);
        return screen;
    }

    /// <summary>A real-ROM machine plus the fake keyboard driving it.</summary>
    private sealed class TestMachine(CpcMachine machine, FakeKeyboard keyboard)
    {
        public CpcMachine Machine { get; } = machine;

        public void RunFrame() => Machine.RunFrame();
        public void PressKey(PhysicalKey key) => keyboard.Press(key);
        public void ReleaseKeys() => keyboard.ReleaseAll();
    }

    private static bool BuildRealMachine(out TestMachine typing, out TestMachine idle)
    {
        typing = null!;
        idle = null!;

        string? path = CpuZ80.TestSupport.RomLocator.Find("Z80CPC.ROM");
        if (path is null) return false;

        byte[] image = AmsdosHeader.Strip(File.ReadAllBytes(path));

        typing = Build(image);
        idle = Build(image);
        return true;

        static TestMachine Build(byte[] image)
        {
            var host = new FakeKeyboard();
            var machine = new CpcMachine(image[..0x4000], image[0x4000..0x8000], keyboard: host);
            machine.Reset();
            return new TestMachine(machine, host);
        }
    }
}
