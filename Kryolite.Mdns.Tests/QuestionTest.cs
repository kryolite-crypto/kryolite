using System.Text;

namespace Kryolite.Mdns.Tests;


public class QuestionTests
{
    [Fact]
    public void QName_ShouldBeRequired()
    {
        var question = new Question { QName = "example.com", QType = 1, QClass = 1 };
        Assert.NotNull(question.QName);
    }

    [Fact]
    public void UR_ShouldReturnFalseWhenQClassIsZero()
    {
        var question = new Question { QName = "example.com", QType = 1, QClass = 0 };
        Assert.False(question.UR);
    }

    [Fact]
    public void UR_ShouldReturnTrueWhenQClassIsNonZero()
    {
        var question = new Question { QName = "example.com", QType = 1, QClass = 15 };
        Assert.True(question.UR);
    }

    [Fact]
    public void UR_SetterShouldSetCorrectValue()
    {
        var question = new Question { QName = "example.com", QType = 1, QClass = 0 };
        question.UR = true;
        Assert.Equal(15, question.QClass & 15);
    }

    [Fact]
    public void ToArray_ShouldReturnCorrectByteArray()
    {
        var question = new Question
        {
            QName = "example.com",
            QType = 1,
            QClass = 1
        };

        var expectedBytes = new List<byte>();
        var parts = question.QName.Split('.');

        foreach (var part in parts)
        {
            expectedBytes.Add((byte)part.Length);
            expectedBytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        expectedBytes.Add(0);
        expectedBytes.AddRange(BitConverter.GetBytes(question.QType.HostToNetworkOrder()));
        expectedBytes.AddRange(BitConverter.GetBytes(question.QClass.HostToNetworkOrder()));

        var result = question.ToArray();

        Assert.Equal(expectedBytes.ToArray(), result);
    }

    [Fact]
    public void Parse_ShouldReturnCorrectQuestion()
    {
        var qName = "example.com";
        var qType = (ushort)1;
        var qClass = (ushort)1;
        var qNameParts = qName.Split('.');

        var bytes = new List<byte>();
        foreach (var part in qNameParts)
        {
            bytes.Add((byte)part.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        bytes.Add(0);
        bytes.AddRange(BitConverter.GetBytes(qType.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(qClass.HostToNetworkOrder()));

        var offset = 0;
        var success = Question.TryParse([.. bytes], ref offset, out var result);

        Assert.True(success);
        Assert.Equal(qName, result!.QName);
        Assert.Equal(qType, result.QType);
        Assert.Equal(qClass, result.QClass);
    }

    [Fact]
    public void Parse_ShouldUpdateOffsetCorrectly()
    {
        var qName = "example.com";
        var qType = (ushort)1;
        var qClass = (ushort)1;
        var qNameParts = qName.Split('.');

        var bytes = new List<byte>();
        foreach (var part in qNameParts)
        {
            bytes.Add((byte)part.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        bytes.Add(0);
        bytes.AddRange(BitConverter.GetBytes(qType.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(qClass.HostToNetworkOrder()));

        var offset = 0;
        Question.TryParse([.. bytes], ref offset, out _);

        Assert.Equal(bytes.Count, offset);
    }

    [Fact]
    public void Parse_ShouldThrowExceptionForInvalidData()
    {
        var invalidBytes = new byte[] { 1, 2, 3 }; // Invalid data
        var offset = 0;

        Assert.False(Question.TryParse(invalidBytes, ref offset, out _));
    }
}
