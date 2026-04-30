using CpuZ80.Core;
using Xunit;

namespace CpuZ80.Tests;

public class RomTests
{
    [Fact]
    public void Read_ReturnsLoadedByte()
    {
        var data = new byte[] { 0x00, 0xAB, 0x00 };
        var rom = new Rom(data);
        Assert.Equal(0xAB, rom.Read(0x0001));
    }

    [Fact]
    public void Write_IsIgnored_NoException()
    {
        var data = new byte[] { 0x11, 0x22, 0x33 };
        var rom = new Rom(data);
        rom.Write(0x0000, 0xFF);
        Assert.Equal(0x11, rom.Read(0x0000));
    }

    [Fact]
    public void Constructor_ClonesInput()
    {
        var data = new byte[] { 0x42 };
        var rom = new Rom(data);
        data[0] = 0xFF;
        Assert.Equal(0x42, rom.Read(0x0000));
    }
}
