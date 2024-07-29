using System.Text;

namespace Kryolite.Module.Mdns.Tests;

public class TXTRecordTest
{
    [Fact]
    public void TYPE_ShouldBeSixteen()
    {
        Assert.Equal(16, TXTRecord.TYPE);
    }

    [Fact]
    public void Text_ShouldBeRequired()
    {
        var record = new TXTRecord { Text = "txtvers=1" };
        Assert.NotNull(record.Text);
    }

    [Fact]
    public void ToArray_ShouldReturnCorrectByteArray()
    {
        var record = new TXTRecord { Text = "txtvers=1" };

        var expectedBytes = new List<byte> { (byte)record.Text.Length };
        expectedBytes.AddRange(Encoding.ASCII.GetBytes(record.Text));

        var result = record.ToArray();

        Assert.Equal(expectedBytes.ToArray(), result);
    }

    [Fact]
    public void Parse_ShouldReturnCorrectTXTRecord()
    {
        var text = "txtvers=1";
        var textBytes = Encoding.ASCII.GetBytes(text);
        var bytes = new List<byte> { (byte)textBytes.Length };
        bytes.AddRange(textBytes);
        var span = new ReadOnlySpan<byte>(bytes.ToArray());

        var result = TXTRecord.Parse(span);

        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Parse_ShouldThrowExceptionForInvalidSpan()
    {
        var invalidBytes = new byte[] { 1 }; // Invalid length byte
        Assert.Throws<ArgumentOutOfRangeException>(() => TXTRecord.Parse(invalidBytes));
    }
}