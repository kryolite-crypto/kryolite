using System.Text;

namespace Kryolite.Module.Mdns;

public class PTRRecord : IRecord
{
    public const ushort TYPE = 12;

    public required string Target { get; set; }

    public byte[] ToArray()
    {
        var ptrEntry = Encoding.ASCII.GetBytes(Target);
        
        List<byte> ptrdata = [];
        ptrdata.Add((byte)ptrEntry.Length);
        ptrdata.AddRange(ptrEntry);
        ptrdata.Add(0);

        return [ ..ptrdata ];
    }

    public static PTRRecord Parse(ReadOnlySpan<byte> span)
    {
        var targetLen = span[0];
        var record = new PTRRecord
        {
            Target = Encoding.ASCII.GetString(span.Slice(1, targetLen))
        };

        return record;
    }
}
