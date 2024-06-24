using System.Net;

namespace Kryolite.Mdns.Tests;

public class DnsPacketTests
{
    [Fact]
    public void ToArray_ShouldReturnCorrectByteArray()
    {
        var packet = new DnsPacket
        {
            Id = 12345,
            Flags = 0,
            QDCount = 1,
            ANCount = 1,
            NSCount = 0,
            ARCount = 0,
            Questions = new[]
            {
                new Question { QName = "example.com", QType = 1, QClass = 1 }
            },
            Answers = new[]
            {
                new Answer { Name = "example.com", Type = 1, Class = 1, TTL = 3600, RDLength = 4, RData = new byte[] { 127, 0, 0, 1 } }
            }
        };

        var expectedBytes = new List<byte>();

        expectedBytes.AddRange(BitConverter.GetBytes(packet.Id.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(packet.Flags.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(packet.QDCount.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(packet.ANCount.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(packet.NSCount.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(packet.ARCount.HostToNetworkOrder()));

        foreach (var question in packet.Questions)
        {
            expectedBytes.AddRange(question.ToArray());
        }

        foreach (var answer in packet.Answers)
        {
            expectedBytes.AddRange(answer.ToArray());
        }

        var result = packet.ToArray();

        Assert.Equal(expectedBytes.ToArray(), result);
    }

    [Fact]
    public void TryParse_ShouldReturnFalseForInvalidData()
    {
        var invalidBytes = new byte[] { 1, 2, 3 }; // Invalid data
        var result = DnsPacket.TryParse(invalidBytes, out var packet);
        Assert.False(result);
        Assert.Null(packet);
    }

    [Fact]
    public void TryParse_ShouldReturnTrueForValidData()
    {
        var question = new Question { QName = "example.com", QType = 1, QClass = 1 };
        var answer = new Answer { Name = "example.com", Type = 1, Class = 1, TTL = 3600, RDLength = 4, RData = new byte[] { 127, 0, 0, 1 } };
        var packet = new DnsPacket
        {
            Id = 12345,
            Flags = 0,
            QDCount = 1,
            ANCount = 1,
            NSCount = 0,
            ARCount = 0,
            Questions = new[] { question },
            Answers = new[] { answer }
        };

        var data = packet.ToArray();

        var result = DnsPacket.TryParse(data, out var parsedPacket);
        Assert.True(result);
        Assert.NotNull(parsedPacket);

        Assert.Equal(packet.Id, parsedPacket.Id);
        Assert.Equal(packet.Flags, parsedPacket.Flags);
        Assert.Equal(packet.QDCount, parsedPacket.QDCount);
        Assert.Equal(packet.ANCount, parsedPacket.ANCount);
        Assert.Equal(packet.NSCount, parsedPacket.NSCount);
        Assert.Equal(packet.ARCount, parsedPacket.ARCount);
        Assert.Single(parsedPacket.Questions);
        Assert.Single(parsedPacket.Answers);
    }

    [Fact]
    public void AddQuestion_ShouldAddQuestionCorrectly()
    {
        var packet = new DnsPacket();
        packet.AddQuestion("example.com", 1);

        Assert.Single(packet.Questions);
        Assert.Equal("example.com", packet.Questions[0].QName);
        Assert.Equal(1, packet.Questions[0].QType);
        Assert.Equal(1, packet.Questions[0].QClass);
    }

    [Fact]
    public void AddAnswer_ShouldAddAnswerCorrectly()
    {
        var packet = new DnsPacket();
        var record = new ARecord { Target = IPAddress.Parse("127.0.0.1") };
        packet.AddAnswer("example.com", ARecord.TYPE, record);

        Assert.Single(packet.Answers);
        Assert.Equal("example.com", packet.Answers[0].Name);
        Assert.Equal(ARecord.TYPE, packet.Answers[0].Type);
        Assert.Equal(1, packet.Answers[0].Class);
        Assert.Equal(0U, packet.Answers[0].TTL);
        Assert.Equal((ushort)4, packet.Answers[0].RDLength);
        Assert.Equal([127, 0, 0, 1], packet.Answers[0].RData);
    }

    [Fact]
    public void AddSRV_ShouldAddSrvRecordCorrectly()
    {
        var packet = new DnsPacket();
        packet.AddSRV("hostname.local", "_rpc._kryolite._tcp.local", 12345);

        Assert.Single(packet.Answers);
        Assert.Equal("hostname.local._rpc._kryolite._tcp.local", packet.Answers[0].Name);
        Assert.Equal(SRVRecord.TYPE, packet.Answers[0].Type);
    }

    [Fact]
    public void AddTXT_ShouldAddTxtRecordCorrectly()
    {
        var packet = new DnsPacket();
        packet.AddTXT("hostname.local", "_rpc._kryolite._tcp.local");

        Assert.Single(packet.Answers);
        Assert.Equal("hostname.local._rpc._kryolite._tcp.local", packet.Answers[0].Name);
        Assert.Equal(TXTRecord.TYPE, packet.Answers[0].Type);
    }

    [Fact]
    public void AddPTR_ShouldAddPtrRecordCorrectly()
    {
        var packet = new DnsPacket();
        packet.AddPTR("hostname.local", "_rpc._kryolite._tcp.local");

        Assert.Single(packet.Answers);
        Assert.Equal("_rpc._kryolite._tcp.local", packet.Answers[0].Name);
        Assert.Equal(PTRRecord.TYPE, packet.Answers[0].Type);
    }

    [Fact]
    public void AddA_ShouldAddARecordCorrectly()
    {
        var packet = new DnsPacket();
        var ipAddress = IPAddress.Parse("127.0.0.1");
        packet.AddA("hostname.local", ipAddress);

        Assert.Single(packet.Answers);
        Assert.Equal("hostname.local", packet.Answers[0].Name);
        Assert.Equal(ARecord.TYPE, packet.Answers[0].Type);
        Assert.Equal(ipAddress.GetAddressBytes(), packet.Answers[0].RData);
    }
}