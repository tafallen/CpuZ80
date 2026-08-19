using Xunit;

namespace Machines.ZxSpectrumPlus3.Tests;

/// <summary>
/// The controller's timing: seek times and the disk's data rate.
/// </summary>
/// <remarks>
/// Timing is opt-in. Without a clock the controller completes everything
/// instantly, which is what +3DOS and AMSDOS need and what most tests use.
/// Loaders that measure how long the controller takes — which is most disk copy
/// protection — need the real thing.
/// </remarks>
public class Upd765aTimingTests
{
    private const ushort StatusPort = 0x2FFD;
    private const ushort DataPort = 0x3FFD;
    private const byte Rqm = 0x80;

    /// <summary>A controller wired to a clock the test drives by hand.</summary>
    private sealed class Clocked
    {
        public ulong Time;
        public readonly Upd765a Fdc;

        public Clocked(bool withDisk = true)
        {
            Fdc = new Upd765a { ClockHz = 4_000_000 };
            Fdc.Clock = () => Time;
            if (withDisk) Fdc.InsertDisk(0, new DiskImage(DskBuilder.Standard()));
            Fdc.MotorOn = true;
        }

        public void Command(params byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                WaitForReady();
                Fdc.Out(DataPort, b);
            }
        }

        public void WaitForReady()
        {
            for (int i = 0; i < 1_000_000 && (Fdc.In(StatusPort) & Rqm) == 0; i++) Time++;
        }

        public byte[] ReadResult(int count)
        {
            var result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                WaitForReady();
                result[i] = Fdc.In(DataPort);
            }
            return result;
        }
    }

    // ── Without a clock ──────────────────────────────────────────────────────

    [Fact]
    public void WithoutAClock_EverythingCompletesInstantly()
    {
        var fdc = new Upd765a();
        fdc.InsertDisk(0, new DiskImage(DskBuilder.Standard()));
        fdc.MotorOn = true;

        foreach (byte b in new byte[] { 0x46, 0x00, 0, 0, 0x41, 2, 0x49, 0x2A, 0xFF })
        {
            fdc.Out(DataPort, b);
        }

        // Every byte is available with no waiting at all.
        for (int i = 0; i < DskBuilder.SectorSize; i++)
        {
            Assert.NotEqual(0, fdc.In(StatusPort) & Rqm);
            fdc.In(DataPort);
        }
    }

    // ── Data rate ────────────────────────────────────────────────────────────

    [Fact]
    public void WithAClock_BytesArriveAtTheDiskDataRate()
    {
        // 32us a byte at 250 kbit/s MFM, which is 128 T-states at 4 MHz.
        var machine = new Clocked();
        machine.Command(0x46, 0x00, 0, 0, 0x41, 2, 0x49, 0x2A, 0xFF);

        machine.WaitForReady();
        ulong start = machine.Time;

        for (int i = 0; i < 100; i++)
        {
            machine.WaitForReady();
            machine.Fdc.In(DataPort);
        }

        ulong elapsed = machine.Time - start;
        ulong expected = 100UL * 128;

        Assert.InRange(elapsed, expected - 256, expected + 256);
    }

    [Fact]
    public void ReadingTooEarlyGivesNothing()
    {
        // The hardware does not hold the byte until asked: a driver that reads
        // without polling gets whatever is on the bus.
        var machine = new Clocked();
        machine.Command(0x46, 0x00, 0, 0, 0x41, 2, 0x49, 0x2A, 0xFF);

        machine.WaitForReady();
        machine.Fdc.In(DataPort);          // consume one byte, starting the delay

        Assert.Equal(0, machine.Fdc.In(StatusPort) & Rqm);
        Assert.Equal(0xFF, machine.Fdc.In(DataPort));
    }

    // ── Seek ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ASeekTakesTimeProportionalToTheDistance()
    {
        var machine = new Clocked();

        // Specify a 2ms step rate: the top nibble counts down from 16.
        machine.Command(0x03, 0xE0, 0x02);
        Assert.Equal(2_000, machine.Fdc.StepRateMicroseconds);

        machine.Command(0x0F, 0x00, 10);   // seek ten tracks

        // Ten steps of 2ms at 4 MHz is 80,000 T-states.
        Assert.NotEqual(0, machine.Fdc.MainStatus & 0x01);   // drive 0 busy

        machine.Time += 79_000;
        machine.Command(0x08);
        Assert.Equal(0x80, machine.ReadResult(1)[0]);        // still seeking: invalid

        machine.Time += 2_000;
        machine.Command(0x08);
        byte[] result = machine.ReadResult(2);

        Assert.Equal(0x20, result[0] & 0x20);   // seek end
        Assert.Equal(10, result[1]);
    }

    [Fact]
    public void AFurtherSeekTakesLonger()
    {
        static ulong SeekTime(int track)
        {
            var machine = new Clocked();
            machine.Command(0x03, 0xE0, 0x02);
            machine.Command(0x0F, 0x00, (byte)track);

            ulong start = machine.Time;
            for (int i = 0; i < 1_000_000; i++)
            {
                machine.Command(0x08);
                if (machine.ReadResult(1)[0] != 0x80) break;
                machine.Time += 1_000;
            }
            return machine.Time - start;
        }

        Assert.True(SeekTime(40) > SeekTime(5),
            "seeking further should take longer, or the head is teleporting");
    }

    [Fact]
    public void TheStepRateComesFromSpecify()
    {
        var machine = new Clocked();

        machine.Command(0x03, 0xF0, 0x02);   // SRT 15 -> 1ms
        Assert.Equal(1_000, machine.Fdc.StepRateMicroseconds);

        machine.Command(0x03, 0x00, 0x02);   // SRT 0 -> 16ms
        Assert.Equal(16_000, machine.Fdc.StepRateMicroseconds);
    }

    [Fact]
    public void AWholeSectorStillReadsCorrectlyWithTimingOn()
    {
        // The point of all this: timing must not corrupt the data.
        var machine = new Clocked();
        machine.Command(0x46, 0x00, 0, 0, 0x43, 2, 0x49, 0x2A, 0xFF);

        var data = new byte[DskBuilder.SectorSize];
        for (int i = 0; i < data.Length; i++)
        {
            machine.WaitForReady();
            data[i] = machine.Fdc.In(DataPort);
        }

        machine.ReadResult(7);

        Assert.All(data, b => Assert.Equal(DskBuilder.FillFor(0, 0x43), b));
    }
}
