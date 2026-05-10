using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests;

public class AddressDecoderTests
{
    private class MockBus : IBus
    {
        public byte LastReadOffset;
        public ushort LastWriteOffset;
        public byte LastWriteValue;
        public byte ReadValue;

        public byte Read(ushort offset) { LastReadOffset = (byte)offset; return ReadValue; }
        public void Write(ushort offset, byte value) { LastWriteOffset = offset; LastWriteValue = value; }
    }

    private readonly AddressDecoder _decoder;
    private readonly MockBus _mockDevice;

    public AddressDecoderTests()
    {
        _decoder = new AddressDecoder();
        _mockDevice = new MockBus();
    }

    [Fact]
    public void Read_UnmappedAddress_ReturnsFF()
    {
        Assert.Equal(0xFF, _decoder.Read(0x1000));
    }

    [Fact]
    public void Write_UnmappedAddress_DoesNothing()
    {
        _decoder.Write(0x1000, 0x42);
        // No exception
    }

    [Fact]
    public void Map_ReadWrite_RoutesToDevice()
    {
        _decoder.Map(0x1000, 0x10FF, _mockDevice);

        _mockDevice.ReadValue = 0xAA;
        Assert.Equal(0xAA, _decoder.Read(0x1005));
        Assert.Equal(0x05, _mockDevice.LastReadOffset);

        _decoder.Write(0x100A, 0x55);
        Assert.Equal(0x0A, _mockDevice.LastWriteOffset);
        Assert.Equal(0x55, _mockDevice.LastWriteValue);
    }

    [Fact]
    public void Map_Overlapping_LastWins()
    {
        var mock1 = new MockBus();
        var mock2 = new MockBus();

        _decoder.Map(0x1000, 0x1FFF, mock1);
        _decoder.Map(0x1800, 0x18FF, mock2);

        mock1.ReadValue = 0x11;
        mock2.ReadValue = 0x22;

        // Address 0x1800 should go to mock2 (offset 0)
        Assert.Equal(0x22, _decoder.Read(0x1800));
        Assert.Equal(0x00, mock2.LastReadOffset);

        // Address 0x1100 should go to mock1 (offset 0x100)
        Assert.Equal(0x11, _decoder.Read(0x1100));
        Assert.Equal(0x00, mock1.LastReadOffset); // 0x1100 - 0x1000 = 0x100. ushort (256)
    }

    [Fact]
    public void Map_InvalidRange_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _decoder.Map(0x2000, 0x1000, _mockDevice));
    }

    [Fact]
    public void Map_ByteAlignedRange_Works()
    {
        // TD-007 implementation: allow byte-level granularity
        _decoder.Map(0x1001, 0x1001, _mockDevice);
        
        _mockDevice.ReadValue = 0x77;
        Assert.Equal(0x77, _decoder.Read(0x1001));
        Assert.Equal(0xFF, _decoder.Read(0x1000));
        Assert.Equal(0xFF, _decoder.Read(0x1002));
    }

    [Fact]
    public void Map_FullRange_Works()
    {
        _decoder.Map(0x0000, 0xFFFF, _mockDevice);
        _mockDevice.ReadValue = 0x99;
        Assert.Equal(0x99, _decoder.Read(0xFFFF));
    }
}
