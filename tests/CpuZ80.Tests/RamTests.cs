using CpuZ80.Core;
using Xunit;

namespace CpuZ80.Tests;

public class RamTests
{
    [Fact]
    public void Load_CopiesDataIntoRam()
    {
        var ram = new Ram(0x10000);
        ram.Load(0x0200, new byte[] { 0x11, 0x22, 0x33 });
        Assert.Equal(0x11, ram.Read(0x0200));
        Assert.Equal(0x22, ram.Read(0x0201));
        Assert.Equal(0x33, ram.Read(0x0202));
    }

    [Fact]
    public void Load_DoesNotAffectSurroundingBytes()
    {
        var ram = new Ram(0x10000);
        ram.Load(0x0200, new byte[] { 0xFF, 0xFF });
        Assert.Equal(0x00, ram.Read(0x01FF));
        Assert.Equal(0x00, ram.Read(0x0202));
    }

    [Fact]
    public void RawBytes_ReflectsWrittenData()
    {
        var ram = new Ram(0x10000);
        ram.Write(0x0100, 0xAB);
        Assert.Equal(0xAB, ram.RawBytes[0x0100]);
    }

    [Fact]
    public void RawBytes_MutationsAreVisibleViaRead()
    {
        var ram = new Ram(0x10000);
        ram.RawBytes[0x0100] = 0xCD;
        Assert.Equal(0xCD, ram.Read(0x0100));
    }
}
