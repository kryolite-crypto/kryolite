using Kryolite.Shared;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class Message
{
    [Key(0)]
    public ulong Id { get; set; }

    [Key(1)]
    [MessagePackFormatter(typeof(PacketFormatter))]
    public object? Payload { get; set; }

    public Message()
    {

    }

    public Message(IPacket packet)
    {
        Id = (uint)Random.Shared.NextInt64();
        Payload = packet;
    }
}
