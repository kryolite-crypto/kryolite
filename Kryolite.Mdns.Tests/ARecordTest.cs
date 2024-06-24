using System.Net;

namespace Kryolite.Mdns.Tests;

public class ARecordTest
{
    [Fact]
    public void TYPE_ShouldBeOne()
    {
        Assert.Equal(1, ARecord.TYPE);
    }

    [Fact]
    public void Target_ShouldBeRequired()
    {
        var record = new ARecord { Target = IPAddress.Parse("192.168.1.1") };
        Assert.NotNull(record.Target);
    }

    [Fact]
    public void ToArray_ShouldReturnIPAddressBytes()
    {
        var ip = IPAddress.Parse("192.168.1.1");
        var record = new ARecord { Target = ip };
        var expectedBytes = ip.GetAddressBytes();

        var result = record.ToArray();

        Assert.Equal(expectedBytes, result);
    }

    [Fact]
    public void Parse_ShouldReturnCorrectARecord()
    {
        var ip = IPAddress.Parse("192.168.1.1");
        var bytes = ip.GetAddressBytes();
        var span = new ReadOnlySpan<byte>(bytes);

        var result = ARecord.Parse(span);

        Assert.Equal(ip, result.Target);
    }

    [Fact]
    public void Parse_ShouldThrowExceptionForInvalidSpan()
    {
        var invalidBytes = new byte[] { 1, 2, 3 }; // Less than 4 bytes
        Assert.Throws<ArgumentException>(() => ARecord.Parse(invalidBytes));
    }
}