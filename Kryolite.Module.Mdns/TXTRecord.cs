using System.Text;

namespace Kryolite.Module.Mdns;

public class TXTRecord : IRecord
{
    public const ushort TYPE = 16;

    public required string Text { get; set; }

    public byte[] ToArray()
    {
        List<byte> txtdata = [];

        var txtEntry = Encoding.ASCII.GetBytes("txtvers=1");
        txtdata.Add((byte)txtEntry.Length);
        txtdata.AddRange(txtEntry);

        return [.. txtdata];
    }

    public static TXTRecord Parse(ReadOnlySpan<byte> span)
    {
        var targetLen = span[0];
        var record = new TXTRecord
        {
            Text = Encoding.ASCII.GetString(span.Slice(1, targetLen))
        };

        return record;
    }
}