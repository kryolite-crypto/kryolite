using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Kryolite.Module.Mdns;

public class Question
{
    public required string QName { get; set; }
    public ushort QType { get; set; }
    public ushort QClass { get; set; }

    public bool UR
    {
        get => (QClass & (15 << 0)) != 0;
        set => QClass |= 15 << 0;
    }

    public byte[] ToArray()
    {
        List<byte> bytes = [];

        var parts = QName.Split(".");

        foreach (var part in parts)
        {
            bytes.Add((byte)part.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(part));
        }

        bytes.Add(0);

        bytes.AddRange(BitConverter.GetBytes(QType.HostToNetworkOrder()));
        bytes.AddRange(BitConverter.GetBytes(QClass.HostToNetworkOrder()));

        return bytes.ToArray();
    }

    public static bool TryParse(byte[] data, ref int offset, [NotNullWhen(true)] out Question? question)
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

            var rType = BitConverter.ToUInt16(data[offset..(offset+2)]).NetworkToHostOrder();
            offset += 2;
            var rClass = BitConverter.ToUInt16(data[offset..(offset+2)]).NetworkToHostOrder();
            offset += 2;

            question = new Question
            {
                QName = string.Join('.', parts),
                QType = rType,
                QClass = rClass
            };

            return true;
        }
        catch (Exception)
        {
            question = null;
            return false;
        }
    }
}
