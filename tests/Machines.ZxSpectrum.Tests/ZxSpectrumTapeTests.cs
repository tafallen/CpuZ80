using Xunit;
using Machines.ZxSpectrum;
using System.IO;

namespace Machines.ZxSpectrum.Tests;

public class ZxSpectrumTapeTests
{
    [Fact]
    public void ReadBit_ReturnsPilotPulses()
    {
        var tape = new ZxSpectrumTapeAdapter();
        byte[] tapData = [ 0x03, 0x00, 0x00, 0xAA, 0xAA ];
        using var ms = new MemoryStream(tapData);
        tape.Load(ms);

        ulong t = 10000; // Arbitrary start
        // First call initializes anchor
        Assert.False(tape.ReadBit(t));
        
        t += 1000;
        Assert.False(tape.ReadBit(t));

        t += 1200; // Crosses 2168
        Assert.True(tape.ReadBit(t));
    }

    [Fact]
    public void ReadBit_GeneratesCorrectSyncPulsesAfterPilot()
    {
        var tape = new ZxSpectrumTapeAdapter();
        byte[] tapData = [ 0x02, 0x00, 0x00, 0x00 ]; 
        using var ms = new MemoryStream(tapData);
        tape.Load(ms);

        ulong t = 10000;
        // Init
        tape.ReadBit(t);

        // Pilot: 8063 pulses of 2168.
        t += (ulong)(8063 * 2168) - 100;
        bool state = tape.ReadBit(t);
        Assert.False(state); // 8062 flips

        t += 200; // Crosses 8063rd pilot pulse
        state = tape.ReadBit(t);
        Assert.True(state); // 8063rd flip

        // Now we are in Sync1 (667 T-states)
        // Remainder t is ~100.
        t += 200; // Total since sync1 start ~300.
        Assert.True(tape.ReadBit(t)); // Should still be true

        t += 400; // Total since sync1 start ~700. Crosses 667.
        state = tape.ReadBit(t);
        Assert.False(state); // Sync1 flip

        // Now Sync2 (735 T-states)
        // Remainder t is ~33.
        t += 300; // Total since sync2 start ~333.
        Assert.False(tape.ReadBit(t)); // Should still be false

        t += 500; // Total since sync2 start ~833. Crosses 735.
        state = tape.ReadBit(t);
        Assert.True(state); // Sync2 flip
    }
}
