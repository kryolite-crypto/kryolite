using System.Text;

namespace Kryolite.Mdns.Tests;

public class AnswerTest
{
    [Fact]
    public void Name_ShouldBeRequired()
    {
        var answer = new Answer { Name = "example.com", Type = 1, Class = 1, TTL = 3600, RDLength = 4, RData = new byte[] { 127, 0, 0, 1 } };
        Assert.NotNull(answer.Name);
    }

    [Fact]
    public void ToArray_ShouldReturnCorrectByteArray()
    {
        var answer = new Answer
        {
            Name = "example.com",
            Type = 1,
            Class = 1,
            TTL = 3600,
            RDLength = 4,
            RData = new byte[] { 127, 0, 0, 1 }
        };

        var expectedBytes = new List<byte>();
        var parts = answer.Name.Split('.');

        foreach (var part in parts)
        {
            expectedBytes.Add((byte)part.Length);
            expectedBytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        expectedBytes.Add(0);

        expectedBytes.AddRange(BitConverter.GetBytes(answer.Type.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(answer.Class.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(answer.TTL.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(answer.RDLength.HostToNetworkOrder()));
        expectedBytes.AddRange(answer.RData);

        var result = answer.ToArray();

        Assert.Equal(expectedBytes.ToArray(), result);
    }

    [Fact]
    public void Parse_ShouldReturnCorrectAnswer()
    {
        var name = "example.com";
        var type = (ushort)1;
        var @class = (ushort)1;
        var ttl = (uint)3600;
        var rdLength = (ushort)4;
        var rData = new byte[] { 127, 0, 0, 1 };
        var nameParts = name.Split('.');

        var bytes = new List<byte>();
        foreach (var part in nameParts)
        {
            bytes.Add((byte)part.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        bytes.Add(0);
        bytes.AddRange(BitConverter.GetBytes(type.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(@class.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(ttl.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(rdLength.HostToNetworkOrder()));
        bytes.AddRange(rData);

        var offset = 0;
        var success = Answer.TryParse([.. bytes], ref offset, out var result);

        Assert.True(success);
        Assert.Equal(name, result!.Name);
        Assert.Equal(type, result.Type);
        Assert.Equal(@class, result.Class);
        Assert.Equal(ttl, result.TTL);
        Assert.Equal(rdLength, result.RDLength);
        Assert.Equal(rData, result.RData);
    }

    [Fact]
    public void Parse_ShouldUpdateOffsetCorrectly()
    {
        var name = "example.com";
        var type = (ushort)1;
        var @class = (ushort)1;
        var ttl = (uint)3600;
        var rdLength = (ushort)4;
        var rData = new byte[] { 127, 0, 0, 1 };
        var nameParts = name.Split('.');

        var bytes = new List<byte>();
        foreach (var part in nameParts)
        {
            bytes.Add((byte)part.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        bytes.Add(0);
        bytes.AddRange(BitConverter.GetBytes(type.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(@class.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(ttl.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(rdLength).Reverse());
        bytes.AddRange(rData);

        var offset = 0;
        Answer.TryParse([.. bytes], ref offset, out _);

        Assert.Equal(bytes.Count, offset);
    }

    [Fact]
    public void Parse_ShouldThrowExceptionForInvalidData()
    {
        var invalidBytes = new byte[] { 1, 2, 3 }; // Invalid data
        var offset = 0;

        Assert.False(Answer.TryParse(invalidBytes, ref offset, out _));
    }
}