using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Kryolite.Mdns;

public class Answer
{
    public required string Name { get; set; }
    public ushort Type { get; set; }
    public ushort Class { get; set; }
    public uint TTL { get; set; }
    public ushort RDLength { get; set; }
    public byte[] RData { get; set; } = [];

    public byte[] ToArray()
    {
        List<byte> bytes = [];

        var parts = Name.Split(".");

        foreach (var part in parts)
        {
            bytes.Add((byte)part.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        bytes.Add(0);

        bytes.AddRange(BitConverter.GetBytes(Type.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(Class.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(TTL.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(RDLength).Reverse());
        bytes.AddRange(RData);

        return bytes.ToArray();
    }

    public static bool TryParse(byte[] data, ref int offset, [NotNullWhen(true)] out Answer? answer)
    {
        try
        {
            var parts = new List<string>();

            while(data[offset] != 0)
            {
                var length = data[offset];

                if (offset + length >= data.Length)
                {
                    break;
                }

                offset++;
                var text = Encoding.ASCII.GetString(data, offset, length);
                offset += length;
                parts.Add(text);
            }

            // Offset null terminator
            offset++;

            var span = data.AsSpan();

            var rType = BitConverter.ToUInt16(span.Slice(offset, 2)).NetworkToHostOrder();
            offset += 2;
            var rClass = BitConverter.ToUInt16(span.Slice(offset, 2)).NetworkToHostOrder();
            offset += 2;
            var ttl = BitConverter.ToUInt32(span.Slice(offset, 4)).NetworkToHostOrder();
            offset += 4;
            var rdlen = BitConverter.ToUInt16(span.Slice(offset, 2)).NetworkToHostOrder();
            offset += 2;

            var rdata = span.Slice(offset, rdlen).ToArray();
            offset += rdlen;

            answer = new Answer
            {
                Name = string.Join('.', parts),
                Type = rType,
                Class = rClass,
                TTL = ttl,
                RDLength = rdlen,
                RData = rdata
            };

            return true;
        }
        catch (Exception)
        {
            answer = null;
            return false;
        }
    }
}
