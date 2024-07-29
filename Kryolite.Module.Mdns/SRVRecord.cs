using System.Net;
using System.Text;

namespace Kryolite.Module.Mdns;

public class SRVRecord : IRecord
{
    public const ushort TYPE = 33;

    public ushort Priority { get; set; }
    public ushort Weight { get; set; }
    public ushort Port { get; set; }
    public required string Target { get; set; }

    public byte[] ToArray()
    {
        List<byte> srvdata = [];
        var srvEntry = Encoding.ASCII.GetBytes(Target);

        srvdata.AddRange(BitConverter.GetBytes(Priority.HostToNetworkOrder()));
        srvdata.AddRange(BitConverter.GetBytes(Weight.HostToNetworkOrder()));
        srvdata.AddRange(BitConverter.GetBytes(Port.HostToNetworkOrder()));
        srvdata.Add((byte)srvEntry.Length);
        srvdata.AddRange(srvEntry);
        srvdata.Add(0);

        return [.. srvdata];
    }

    public static SRVRecord Parse(ReadOnlySpan<byte> span)
    {
        var targetLen = span[6];
        var record = new SRVRecord
        {
            Priority = BitConverter.ToUInt16(span).NetworkToHostOrder(),
            Weight = BitConverter.ToUInt16(span[2..]).NetworkToHostOrder(),
            Port = BitConverter.ToUInt16(span[4..]).NetworkToHostOrder(),
            Target = Encoding.ASCII.GetString(span.Slice(7, targetLen))
        };

        return record;
    }
}
