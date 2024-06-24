using System.Text;

namespace Kryolite.Mdns.Tests;

public class PTRRecordTest
{
    [Fact]
    public void TYPE_ShouldBeTwelve()
    {
        Assert.Equal(12, PTRRecord.TYPE);
    }

    [Fact]
    public void Target_ShouldBeRequired()
    {
        var record = new PTRRecord { Target = "example.com" };
        Assert.NotNull(record.Target);
    }

    [Fact]
    public void ToArray_ShouldReturnCorrectByteArray()
    {
        var target = "example.com";
        var record = new PTRRecord { Target = target };

        var expectedBytes = new List<byte> { (byte)target.Length };
        expectedBytes.AddRange(Encoding.ASCII.GetBytes(target));
        expectedBytes.Add(0);

        var result = record.ToArray();

        Assert.Equal(expectedBytes.ToArray(), result);
    }

    [Fact]
    public void Parse_ShouldReturnCorrectPTRRecord()
    {
        var target = "example.com";
        
        var targetBytes = Encoding.ASCII.GetBytes(target);
        var bytes = new List<byte>();
        
        bytes.Add((byte)target.Length);
        bytes.AddRange(targetBytes);
        bytes.Add(0);

        var result = PTRRecord.Parse(bytes.ToArray());

        Assert.Equal(target, result.Target);
    }

    [Fact]
    public void Parse_ShouldThrowExceptionForInvalidSpan()
    {
        var invalidBytes = new byte[] { 1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => PTRRecord.Parse(invalidBytes));
    }
}