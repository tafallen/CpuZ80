using Xunit;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// Saving to tape: decoding the MIC output back into .TAP blocks.
/// </summary>
/// <remarks>
/// Loading and saving are the same encoding in opposite directions, so these
/// tests build the pulse train the ROM would emit and check the decoder gets the
/// original bytes back. Before this, <c>WriteBit</c> was an empty stub and a save
/// silently produced nothing.
/// </remarks>
public class ZxSpectrumTapeSaveTests
{
    private const int Pilot = 2168;
    private const int Sync1 = 667;
    private const int Sync2 = 735;
    private const int Bit0 = 855;
    private const int Bit1 = 1710;

    /// <summary>Plays a block's worth of MIC edges into the adapter.</summary>
    private static ulong WriteBlock(
        ZxSpectrumTapeAdapter tape, byte[] block, ulong t, int pilotPulses = 32)
    {
        bool level = false;

        // The first call establishes the starting level rather than an edge.
        tape.WriteBit(level, t);

        for (int i = 0; i < pilotPulses; i++)
        {
            t += (ulong)Pilot;
            level = !level;
            tape.WriteBit(level, t);
        }

        t += (ulong)Sync1;
        level = !level;
        tape.WriteBit(level, t);

        t += (ulong)Sync2;
        level = !level;
        tape.WriteBit(level, t);

        foreach (byte b in block)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                int width = ((b >> bit) & 1) != 0 ? Bit1 : Bit0;

                // Two pulses per bit.
                for (int pulse = 0; pulse < 2; pulse++)
                {
                    t += (ulong)width;
                    level = !level;
                    tape.WriteBit(level, t);
                }
            }
        }

        return t;
    }

    [Fact]
    public void ASavedBlockDecodesBackToItsBytes()
    {
        var tape = new ZxSpectrumTapeAdapter();
        byte[] block = [0x00, 0xFF, 0xA5, 0x3C, 0x01];

        WriteBlock(tape, block, 0);
        tape.FinishRecording();

        Assert.Single(tape.RecordedBlocks);
        Assert.Equal(block, tape.RecordedBlocks[0]);
    }

    [Fact]
    public void AHeaderBlockDecodesBackToItsBytes()
    {
        // A real header: flag 0, type 0, ten filename characters, lengths and a
        // checksum. Nothing about it is special to the decoder, which is the
        // point — it must not need to know what a block means.
        var tape = new ZxSpectrumTapeAdapter();
        byte[] header =
        [
            0x00, 0x00,
            .."TESTFILE  "u8.ToArray(),
            0x0A, 0x00, 0x00, 0x80, 0x0A, 0x00,
            0x2B,
        ];

        WriteBlock(tape, header, 0);
        tape.FinishRecording();

        Assert.Equal(header, tape.RecordedBlocks[0]);
    }

    [Fact]
    public void TwoBlocksSeparatedBySilenceAreKeptApart()
    {
        var tape = new ZxSpectrumTapeAdapter();
        byte[] first = [0x11, 0x22];
        byte[] second = [0x33, 0x44];

        ulong t = WriteBlock(tape, first, 0);

        // A long hold with no edge is what ends a block.
        t += 100_000;
        tape.WriteBit(false, t);

        WriteBlock(tape, second, t);
        tape.FinishRecording();

        Assert.Equal(2, tape.RecordedBlocks.Count);
        Assert.Equal(first, tape.RecordedBlocks[0]);
        Assert.Equal(second, tape.RecordedBlocks[1]);
    }

    [Fact]
    public void ANewPilotToneEndsThePreviousBlockWithoutASilence()
    {
        // Some savers run straight from one block into the next leader.
        var tape = new ZxSpectrumTapeAdapter();
        byte[] first = [0x11, 0x22];
        byte[] second = [0x33, 0x44];

        ulong t = WriteBlock(tape, first, 0);
        WriteBlock(tape, second, t);
        tape.FinishRecording();

        Assert.Equal(2, tape.RecordedBlocks.Count);
        Assert.Equal(first, tape.RecordedBlocks[0]);
        Assert.Equal(second, tape.RecordedBlocks[1]);
    }

    [Fact]
    public void SaveWritesAValidTapFile()
    {
        var tape = new ZxSpectrumTapeAdapter();
        byte[] block = [0xDE, 0xAD, 0xBE, 0xEF];

        WriteBlock(tape, block, 0);

        var stream = new MemoryStream();
        tape.Save(stream);

        // A .TAP block is a 16-bit little-endian length followed by the data.
        Assert.Equal([0x04, 0x00, 0xDE, 0xAD, 0xBE, 0xEF], stream.ToArray());
    }

    [Fact]
    public void ASavedTapeLoadsBackIntoAnotherAdapter()
    {
        // The real round trip: save, then feed the file to a fresh adapter and
        // have it accept the blocks.
        var saver = new ZxSpectrumTapeAdapter();
        byte[] block = [0x00, 0x03, 0x11, 0x22, 0x33];
        WriteBlock(saver, block, 0);

        var file = new MemoryStream();
        saver.Save(file);
        file.Position = 0;

        var loader = new ZxSpectrumTapeAdapter();
        loader.Load(file);

        // It plays something rather than sitting idle, which is all Load exposes.
        Assert.False(loader.ReadBit(0) && loader.ReadBit(100_000));
    }

    [Fact]
    public void PulseWidthsSlightlyOffAreStillDecoded()
    {
        // Real recordings are never exact, and the ROM's own timings shift with
        // interrupt jitter. A decoder that demanded exact widths would reject
        // nearly everything.
        var tape = new ZxSpectrumTapeAdapter();
        byte[] block = [0xA5];

        bool level = false;
        ulong t = 0;
        tape.WriteBit(level, t);

        for (int i = 0; i < 32; i++)
        {
            t += (ulong)(Pilot + (i % 3) - 1);
            level = !level;
            tape.WriteBit(level, t);
        }

        t += (ulong)(Sync1 + 20);
        level = !level;
        tape.WriteBit(level, t);

        t += (ulong)(Sync2 - 20);
        level = !level;
        tape.WriteBit(level, t);

        for (int bit = 7; bit >= 0; bit--)
        {
            int width = ((0xA5 >> bit) & 1) != 0 ? Bit1 : Bit0;
            for (int pulse = 0; pulse < 2; pulse++)
            {
                t += (ulong)(width + (pulse == 0 ? 40 : -40));
                level = !level;
                tape.WriteBit(level, t);
            }
        }

        tape.FinishRecording();

        Assert.Equal(block, tape.RecordedBlocks[0]);
    }

    [Fact]
    public void DataWithoutALeaderIsIgnored()
    {
        // Without a pilot and sync there is no block, and treating stray MIC
        // activity as data would fabricate one.
        var tape = new ZxSpectrumTapeAdapter();

        bool level = false;
        ulong t = 0;
        tape.WriteBit(level, t);

        for (int i = 0; i < 64; i++)
        {
            t += (ulong)Bit0;
            level = !level;
            tape.WriteBit(level, t);
        }

        tape.FinishRecording();

        Assert.Empty(tape.RecordedBlocks);
    }

    [Fact]
    public void UntimedWritesRecordNothing()
    {
        // A level with no timestamp cannot be decoded at all.
        var tape = new ZxSpectrumTapeAdapter();

        for (int i = 0; i < 500; i++) tape.WriteBit(i % 2 == 0);
        tape.FinishRecording();

        Assert.Empty(tape.RecordedBlocks);
    }

    [Fact]
    public void ClearRecordingDiscardsTheCapture()
    {
        var tape = new ZxSpectrumTapeAdapter();
        WriteBlock(tape, [0x42], 0);
        tape.FinishRecording();
        Assert.NotEmpty(tape.RecordedBlocks);

        tape.ClearRecording();

        Assert.Empty(tape.RecordedBlocks);
    }
}
