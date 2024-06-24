using System.Text;

namespace Kryolite.Mdns.Tests;

public class SRVRecordTest
{
    [Fact]
    public void TYPE_ShouldBeThirtyThree()
    {
        Assert.Equal(33, SRVRecord.TYPE);
    }

    [Fact]
    public void Target_ShouldBeRequired()
    {
        var record = new SRVRecord { Target = "example.com" };
        Assert.NotNull(record.Target);
    }

    [Fact]
    public void ToArray_ShouldReturnCorrectByteArray()
    {
        var record = new SRVRecord
        {
            Priority = 10,
            Weight = 20,
            Port = 30,
            Target = "example.com"
        };

        var targetBytes = Encoding.ASCII.GetBytes(record.Target);
        var expectedBytes = new List<byte>();

        expectedBytes.AddRange(BitConverter.GetBytes(record.Priority.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(record.Weight.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(record.Port.HostToNetworkOrder()));
        expectedBytes.Add((byte)targetBytes.Length);
        expectedBytes.AddRange(targetBytes);
        expectedBytes.Add(0);

        var result = record.ToArray();

        Assert.Equal(expectedBytes.ToArray(), result);
    }

    [Fact]
    public void Parse_ShouldReturnCorrectSRVRecord()
    {
        var target = "example.com";
        var targetBytes = Encoding.ASCII.GetBytes(target);
        var bytes = new List<byte>();

        bytes.AddRange(BitConverter.GetBytes(((ushort)10).HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(((ushort)20).HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(((ushort)30).HostToNetworkOrder()));
        bytes.Add((byte)targetBytes.Length);
        bytes.AddRange(targetBytes);
        bytes.Add(0);

        var result = SRVRecord.Parse(bytes.ToArray());

        Assert.Equal((ushort)10, result.Priority);
        Assert.Equal((ushort)20, result.Weight);
        Assert.Equal((ushort)30, result.Port);
        Assert.Equal(target, result.Target);
    }

    [Fact]
    public void Parse_ShouldThrowExceptionForInvalidSpan()
    {
        var invalidBytes = new byte[] { 0 };

        Assert.Throws<IndexOutOfRangeException>(() => SRVRecord.Parse(invalidBytes));
    }
}