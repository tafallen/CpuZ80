using CpuZ80.Core;
using Xunit;

namespace CpuZ80.Tests;

public class AddressDecoderTests
{
    [Fact]
    public void Read_SingleMapping_ReturnsDeviceData()
    {
        var ram = new Ram(0x100);
        ram.Write(0x0010, 0xAB);
        var decoder = new AddressDecoder();
        decoder.Map(0x0000, 0x00FF, ram);
        Assert.Equal(0xAB, decoder.Read(0x0010));
    }

    [Fact]
    public void Write_SingleMapping_WritesToDevice()
    {
        var ram = new Ram(0x100);
        var decoder = new AddressDecoder();
        decoder.Map(0x0000, 0x00FF, ram);
        decoder.Write(0x0010, 0xCD);
        Assert.Equal(0xCD, ram.Read(0x0010));
    }

    [Fact]
    public void Read_Unmapped_Returns0xFF()
    {
        var decoder = new AddressDecoder();
        Assert.Equal(0xFF, decoder.Read(0x1000));
    }

    [Fact]
    public void Write_Unmapped_IsSilent()
    {
        var decoder = new AddressDecoder();
        var ex = Record.Exception((Action)(() => decoder.Write(0x1000, 0x42)));
        Assert.Null(ex);
    }

    [Fact]
    public void Read_MultipleNonOverlapping_RoutesCorrectly()
    {
        var ram1 = new Ram(0x100);
        var ram2 = new Ram(0x100);
        ram1.Write(0x0000, 0x11);
        ram2.Write(0x0000, 0x22);
        var decoder = new AddressDecoder();
        decoder.Map(0x0000, 0x00FF, ram1);
        decoder.Map(0x0100, 0x01FF, ram2);
        Assert.Equal(0x11, decoder.Read(0x0000));
        Assert.Equal(0x22, decoder.Read(0x0100));
    }

    [Fact]
    public void Read_Overlap_LastRegistrationWins()
    {
        var first = new Ram(0x100);
        var second = new Ram(0x100);
        first.Write(0x0000, 0x11);
        second.Write(0x0000, 0x22);
        var decoder = new AddressDecoder();
        decoder.Map(0x0000, 0x00FF, first);
        decoder.Map(0x0000, 0x00FF, second);
        Assert.Equal(0x22, decoder.Read(0x0000));
    }

    [Fact]
    public void Device_SeesZeroBasedOffset()
    {
        var ram = new Ram(0x100);
        var decoder = new AddressDecoder();
        decoder.Map(0x4000, 0x40FF, ram);
        decoder.Write(0x4001, 0x55);
        Assert.Equal(0x55, ram.Read(0x0001));
        Assert.Equal(0x00, ram.Read(0x4001 - 0x4000 + 1)); // same thing expressed differently
    }
}
