using Xunit;
using Machines.AmstradCpc;
using Machines.Common;

namespace Machines.AmstradCpc.Tests;

/// <summary>
/// What actually differs between a 464, a 664 and a 6128.
/// </summary>
/// <remarks>
/// The three share a motherboard design. The differences that reach the
/// emulation are the amount of RAM, whether banking does anything at all,
/// whether a drive is fitted, and whether a cassette is.
/// </remarks>
public class CpcModelTests
{
    private sealed class NullVideo : IVideoSink
    {
        public void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height) { }
    }

    private static CpcMachine Build(CpcModel model, ITapeDevice? tape = null)
    {
        var machine = new CpcMachine(model, CpcBootTests.TestRom(), CpcBootTests.TestRom(), tape: tape);
        machine.Reset();
        return machine;
    }

    // ── Memory ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CpcModel.Cpc464, 4)]
    [InlineData(CpcModel.Cpc664, 4)]
    [InlineData(CpcModel.Cpc6128, 8)]
    public void OnlyThe6128HasTheSecond64K(CpcModel model, int banks)
    {
        var machine = Build(model);

        Assert.Equal(banks, machine.Memory.Banks.Length);
        Assert.Equal(model == CpcModel.Cpc6128, machine.Memory.Has128K);
    }

    [Theory]
    [InlineData(CpcModel.Cpc464)]
    [InlineData(CpcModel.Cpc664)]
    public void A64KMachineIgnoresTheBankingRegisterEntirely(CpcModel model)
    {
        // Banking is decoded by the expansion PAL, which a 64K machine does not
        // have — so the register does nothing rather than doing something
        // approximate. Masking the bank number instead would put base bank 3 at
        // 0x4000 in configuration 3, on a machine that cannot bank at all.
        var machine = Build(model);

        for (int config = 0; config < 8; config++)
        {
            machine.WritePort(0x7F00, (byte)(0xC0 | config));

            for (int window = 0; window < 4; window++)
            {
                Assert.Equal(window, machine.Memory.BankAt(window));
            }
        }
    }

    [Fact]
    public void A6128BanksProperly()
    {
        var machine = Build(CpcModel.Cpc6128);

        machine.WritePort(0x7F00, 0xC2);   // configuration 2: banks 4,5,6,7

        Assert.Equal(4, machine.Memory.BankAt(0));
        Assert.Equal(7, machine.Memory.BankAt(3));
    }

    // ── Storage ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CpcModel.Cpc464, false)]
    [InlineData(CpcModel.Cpc664, true)]
    [InlineData(CpcModel.Cpc6128, true)]
    public void OnlyTheDiskModelsFitAController(CpcModel model, bool hasDrive)
    {
        var machine = Build(model);

        Assert.Equal(hasDrive, machine.Fdc is not null);
    }

    [Fact]
    public void TheControllerSitsAtTheCpcsOwnAddresses()
    {
        // &FB7E for main status, not the +3's 0x2FFD. Reusing the +3's decode
        // would leave the controller unreachable on this machine.
        var machine = Build(CpcModel.Cpc664);

        Assert.NotEqual(0xFF, machine.ReadPort(0xFB7E));
        Assert.Equal(machine.Fdc!.MainStatus, machine.ReadPort(0xFB7E));
    }

    [Fact]
    public void TheMotorLatchIsSeparateFromTheController()
    {
        // &FA7E is a latch of its own, where the +3 keeps the motor bit in its
        // paging port. One motor line drives both drives.
        var machine = Build(CpcModel.Cpc664);

        Assert.False(machine.Fdc!.MotorOn);

        machine.WritePort(0xFA7E, 0x01);
        Assert.True(machine.Fdc.MotorOn);

        machine.WritePort(0xFA7E, 0x00);
        Assert.False(machine.Fdc.MotorOn);
    }

    [Fact]
    public void A464HasNothingOnTheDiskPorts()
    {
        var machine = Build(CpcModel.Cpc464);

        Assert.Equal(0xFF, machine.ReadPort(0xFB7E));
    }

    // ── Cassette ─────────────────────────────────────────────────────────────

    private sealed class FakeTape : ITapeDevice
    {
        public bool Level = true;
        public readonly List<bool> Written = [];

        public bool ReadBit(ulong currentTState) => Level;
        public void WriteBit(bool bit) { }
        public void WriteBit(bool bit, ulong currentTState) => Written.Add(bit);
        public void Load(Stream data) { }
    }

    [Fact]
    public void TheCassetteMotorIsPortCBit4()
    {
        // Bit 4 is the motor and bit 5 the write data, that way round. The
        // obvious guess is the other way, and it leaves a 464 unable to load
        // anything while the deck appears to run.
        var machine = Build(CpcModel.Cpc464);

        machine.WritePort(0xF700, 0x80);   // everything an output
        machine.WritePort(0xF600, 0x10);

        Assert.True(machine.Ppi.TapeMotorOn);
        Assert.False(machine.Ppi.TapeOutput);

        machine.WritePort(0xF600, 0x20);
        Assert.False(machine.Ppi.TapeMotorOn);
        Assert.True(machine.Ppi.TapeOutput);
    }

    [Fact]
    public void TheCassetteReadLineReachesPortBBit7()
    {
        var machine = Build(CpcModel.Cpc464);

        machine.Ppi.TapeInput = true;
        Assert.Equal(0x80, machine.ReadPort(0xF500) & 0x80);

        machine.Ppi.TapeInput = false;
        Assert.Equal(0x00, machine.ReadPort(0xF500) & 0x80);
    }

    [Fact]
    public void TheTapeIsSampledWhileTheMachineRuns()
    {
        // The firmware measures how long the level holds, so a read line that
        // only updates once a frame carries no data at all.
        var tape = new FakeTape { Level = false };
        var machine = Build(CpcModel.Cpc464, tape);

        machine.WritePort(0xF700, 0x80);
        machine.WritePort(0xF600, 0x10);   // motor on

        machine.RunFrame();

        Assert.True(machine.Ppi.TapeInput);
    }

    [Fact]
    public void NothingIsSampledWithTheMotorOff()
    {
        var tape = new FakeTape { Level = false };
        var machine = Build(CpcModel.Cpc464, tape);

        machine.RunFrame();

        Assert.False(machine.Ppi.TapeInput);
    }

    [Fact]
    public void WritingTheCassetteLineReachesTheTape()
    {
        var tape = new FakeTape();
        var machine = Build(CpcModel.Cpc464, tape);

        machine.WritePort(0xF700, 0x80);
        machine.WritePort(0xF600, 0x10);   // motor on
        machine.WritePort(0xF600, 0x30);   // motor on, write high
        machine.WritePort(0xF600, 0x10);   // write low

        Assert.Contains(true, tape.Written);
        Assert.Contains(false, tape.Written);
    }

    // ── Port B's other lines ─────────────────────────────────────────────────

    [Fact]
    public void PortBReportsTheExpansionAndPrinterLines()
    {
        var machine = Build(CpcModel.Cpc6128);

        machine.Ppi.ExpansionPresent = true;
        Assert.Equal(0x20, machine.ReadPort(0xF500) & 0x20);

        machine.Ppi.PrinterReady = true;
        Assert.Equal(0x40, machine.ReadPort(0xF500) & 0x40);
    }

    [Fact]
    public void PortBReportsTheRefreshRateLink()
    {
        var machine = Build(CpcModel.Cpc6128);

        Assert.Equal(0x10, machine.ReadPort(0xF500) & 0x10);   // 50 Hz

        machine.Ppi.Is50Hz = false;
        Assert.Equal(0x00, machine.ReadPort(0xF500) & 0x10);
    }
}
