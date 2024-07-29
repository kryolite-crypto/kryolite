using System.Net;

namespace Kryolite.Module.Mdns;

public class ARecord : IRecord
{
    public const ushort TYPE = 1;

    public required IPAddress Target { get; set; }

    public byte[] ToArray()
    {
        return Target.GetAddressBytes();
    }

    public static ARecord Parse(ReadOnlySpan<byte> span)
    {
        var record = new ARecord
        {
            Target = new IPAddress(span)
        };

        return record;
    }
}
