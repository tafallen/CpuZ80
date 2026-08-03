using Machines.Common;
using Machines.Zx80;
using Xunit;

namespace Machines.Zx80.Tests;

/// <summary>
/// Keyboard tests drive the machine via a stub IPhysicalKeyboard and assert
/// that IN reads on the correct port address return the right active-low byte.
/// </summary>
public class Zx80KeyboardTests
{
    // Minimal 4K NOP ROM.
    private static byte[] NopRom() => new byte[0x1000];

    // Stub keyboard: caller sets which keys are down.
    private sealed class StubKeyboard : IPhysicalKeyboard
    {
        private readonly HashSet<PhysicalKey> _down = new();
        public void Press(PhysicalKey key)   => _down.Add(key);
        public bool IsKeyDown(PhysicalKey key) => _down.Contains(key);
    }

    // Helper: read a keyboard half-row via the machine's port bus.
    // The ZX80 ROM reads IN with the high byte selecting the row (bit low = row active).
    // Port address = (highByte << 8) | 0xFE.
    private static byte ReadRow(Zx80Machine machine, byte highByte)
    {
        ushort port = (ushort)((highByte << 8) | 0xFE);
        return machine.ReadPort(port);
    }

    [Fact]
    public void Keyboard_NoKeysPressed_AllBitsHigh()
    {
        var kb = new StubKeyboard();
        var machine = new Zx80Machine(NopRom(), keyboard: kb);
        // All half-rows should return 0xFF when nothing is pressed.
        Assert.Equal(0xFF, ReadRow(machine, 0xFE)); // A8
        Assert.Equal(0xFF, ReadRow(machine, 0xFD)); // A9
        Assert.Equal(0xFF, ReadRow(machine, 0xFB)); // A10
        Assert.Equal(0xFF, ReadRow(machine, 0xF7)); // A11
        Assert.Equal(0xFF, ReadRow(machine, 0xEF)); // A12
        Assert.Equal(0xFF, ReadRow(machine, 0xDF)); // A13
        Assert.Equal(0xFF, ReadRow(machine, 0xBF)); // A14
        Assert.Equal(0xFF, ReadRow(machine, 0x7F)); // A15
    }

    // Theory: each half-row, one key pressed — asserts correct bit goes low.
    // Data: (highByte, PhysicalKey, expectedBitMask)
    // expectedBitMask is the bit that should be 0 (e.g. bit0 pressed → result & 0x01 == 0).
    [Theory]
    // A8 row: Shift(0), Z(1), X(2), C(3), V(4)
    [InlineData(0xFE, PhysicalKey.LeftShift, 0b00000001)]
    [InlineData(0xFE, PhysicalKey.Z,         0b00000010)]
    [InlineData(0xFE, PhysicalKey.X,         0b00000100)]
    [InlineData(0xFE, PhysicalKey.C,         0b00001000)]
    [InlineData(0xFE, PhysicalKey.V,         0b00010000)]
    // A9 row: A(0), S(1), D(2), F(3), G(4)
    [InlineData(0xFD, PhysicalKey.A,         0b00000001)]
    [InlineData(0xFD, PhysicalKey.S,         0b00000010)]
    [InlineData(0xFD, PhysicalKey.D,         0b00000100)]
    [InlineData(0xFD, PhysicalKey.F,         0b00001000)]
    [InlineData(0xFD, PhysicalKey.G,         0b00010000)]
    // A10 row: Q(0), W(1), E(2), R(3), T(4)
    [InlineData(0xFB, PhysicalKey.Q,         0b00000001)]
    [InlineData(0xFB, PhysicalKey.W,         0b00000010)]
    [InlineData(0xFB, PhysicalKey.E,         0b00000100)]
    [InlineData(0xFB, PhysicalKey.R,         0b00001000)]
    [InlineData(0xFB, PhysicalKey.T,         0b00010000)]
    // A11 row: 1(0), 2(1), 3(2), 4(3), 5(4)
    [InlineData(0xF7, PhysicalKey.D1,        0b00000001)]
    [InlineData(0xF7, PhysicalKey.D2,        0b00000010)]
    [InlineData(0xF7, PhysicalKey.D3,        0b00000100)]
    [InlineData(0xF7, PhysicalKey.D4,        0b00001000)]
    [InlineData(0xF7, PhysicalKey.D5,        0b00010000)]
    // A12 row: 0(0), 9(1), 8(2), 7(3), 6(4)
    [InlineData(0xEF, PhysicalKey.D0,        0b00000001)]
    [InlineData(0xEF, PhysicalKey.D9,        0b00000010)]
    [InlineData(0xEF, PhysicalKey.D8,        0b00000100)]
    [InlineData(0xEF, PhysicalKey.D7,        0b00001000)]
    [InlineData(0xEF, PhysicalKey.D6,        0b00010000)]
    // A13 row: P(0), O(1), I(2), U(3), Y(4)
    [InlineData(0xDF, PhysicalKey.P,         0b00000001)]
    [InlineData(0xDF, PhysicalKey.O,         0b00000010)]
    [InlineData(0xDF, PhysicalKey.I,         0b00000100)]
    [InlineData(0xDF, PhysicalKey.U,         0b00001000)]
    [InlineData(0xDF, PhysicalKey.Y,         0b00010000)]
    // A14 row: NEWLINE(0), L(1), K(2), J(3), H(4)
    [InlineData(0xBF, PhysicalKey.Return,    0b00000001)]
    [InlineData(0xBF, PhysicalKey.L,         0b00000010)]
    [InlineData(0xBF, PhysicalKey.K,         0b00000100)]
    [InlineData(0xBF, PhysicalKey.J,         0b00001000)]
    [InlineData(0xBF, PhysicalKey.H,         0b00010000)]
    // A15 row: Space(0), Symbol Shift(1), M(2), N(3), B(4)
    [InlineData(0x7F, PhysicalKey.Space,      0b00000001)]
    [InlineData(0x7F, PhysicalKey.RightShift, 0b00000010)]
    [InlineData(0x7F, PhysicalKey.M,          0b00000100)]
    [InlineData(0x7F, PhysicalKey.N,          0b00001000)]
    [InlineData(0x7F, PhysicalKey.B,          0b00010000)]
    public void Keyboard_HalfRow_CorrectBitLow(byte highByte, PhysicalKey key, byte pressedBit)
    {
        var kb = new StubKeyboard();
        kb.Press(key);
        var machine = new Zx80Machine(NopRom(), keyboard: kb);
        byte result = ReadRow(machine, highByte);
        Assert.Equal(0, result & pressedBit);           // pressed bit is low
        Assert.Equal(0xFF & ~pressedBit, result & ~pressedBit); // all other bits high
    }

    [Fact]
    public void Keyboard_MultipleHalfRowsSelected_CombinesResults()
    {
        // Select A8 (bit A8 low) and A9 (bit A9 low) simultaneously: highByte = 0xFC.
        // Press Shift (A8 bit0) and A (A9 bit0) — both should appear low.
        var kb = new StubKeyboard();
        kb.Press(PhysicalKey.LeftShift);
        kb.Press(PhysicalKey.A);
        var machine = new Zx80Machine(NopRom(), keyboard: kb);
        ushort port = (ushort)((0xFC << 8) | 0xFE); // both A8 and A9 low
        byte result = machine.ReadPort(port);
        Assert.Equal(0, result & 0x01); // bit0 low (Shift from A8 AND A from A9 both bit0)
    }

    [Fact]
    public void Keyboard_KeyInWrongRow_NotReflected()
    {
        // Press Z (A8 row, bit1). Read A9 row. Z must not appear.
        var kb = new StubKeyboard();
        kb.Press(PhysicalKey.Z);
        var machine = new Zx80Machine(NopRom(), keyboard: kb);
        byte result = ReadRow(machine, 0xFD); // A9 row
        Assert.Equal(0xFF, result);
    }

    [Fact]
    public void Keyboard_NoKeyboard_ReturnsAllBitsHigh()
    {
        // Passing null keyboard: all rows return 0xFF.
        var machine = new Zx80Machine(NopRom());
        Assert.Equal(0xFF, ReadRow(machine, 0xFE));
    }

    /// <summary>Counts how many times the host is asked for key state.</summary>
    private sealed class CountingKeyboard : IPhysicalKeyboard
    {
        public int Queries;
        public bool IsKeyDown(PhysicalKey key) { Queries++; return false; }
    }

    // The matrix is 8 rows x 5 keys, so one full scan is 40 host queries.
    private const int QueriesPerScan = 40;

    [Fact]
    public void Keyboard_ManyReadsInOneFrame_ScanTheHostOnlyOnce()
    {
        // Under the real Raylib adapter each host query is a native P/Invoke, so
        // a game polling the keyboard in a tight IN loop must not scale the
        // number of them with the number of reads.
        var kb = new CountingKeyboard();
        var machine = new Zx80Machine(NopRom(), keyboard: kb);

        for (int i = 0; i < 500; i++) ReadRow(machine, 0xFE);

        Assert.Equal(QueriesPerScan, kb.Queries);
    }

    [Fact]
    public void Keyboard_IsRescannedOncePerFrame()
    {
        var kb = new CountingKeyboard();
        var machine = new Zx80Machine(NopRom(), keyboard: kb);

        ReadRow(machine, 0xFE);
        Assert.Equal(QueriesPerScan, kb.Queries);

        // A new frame must pick up whatever the host now reports.
        machine.RunFrame();
        ReadRow(machine, 0xFE);
        Assert.Equal(QueriesPerScan * 2, kb.Queries);

        // Still only one scan for the rest of this frame.
        for (int i = 0; i < 100; i++) ReadRow(machine, 0xFD);
        Assert.Equal(QueriesPerScan * 2, kb.Queries);
    }

    [Fact]
    public void Keyboard_KeyPressedBetweenFrames_IsSeenAfterTheFrameBoundary()
    {
        var kb = new StubKeyboard();
        var machine = new Zx80Machine(NopRom(), keyboard: kb);

        Assert.Equal(0xFF, ReadRow(machine, 0xFE)); // nothing down yet

        kb.Press(PhysicalKey.Z); // A8 row, bit 1
        machine.RunFrame();

        Assert.Equal(0, ReadRow(machine, 0xFE) & 0b00000010);
    }
}
