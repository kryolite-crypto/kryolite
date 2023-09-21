using Kryolite.Shared;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class Message : IMessage
{
    [Key(0)]
    public ulong Id { get; set; }

    [Key(1)]
    [MessagePackFormatter(typeof(PacketFormatter))]
    public object? Payload { get; set; }

    public Message()
    {

    }

    public Message(ulong id, IPacket packet)
    {
        Id = id;
        Payload = packet;
    }

    public Message(IPacket packet)
    {
        Id = (uint)Random.Shared.NextInt64();
        Payload = packet;
    }
}

[MessagePackObject]
public class Reply
{
    [Key(0)]
    public ulong Id { get; set; }

    [Key(1)]
    [MessagePackFormatter(typeof(PacketFormatter))]
    public object? Payload { get; set; }

    [Key(2)]
    public ulong ReplyTo { get; set; }

    public Reply()
    {

    }

    public Reply(ulong id, ulong replyTo, IPacket packet)
    {
        Id = id;
        ReplyTo = replyTo;
        Payload = packet;
    }

    public Reply(ulong replyTo, IPacket packet)
    {
        Id = (uint)Random.Shared.NextInt64();
        ReplyTo = replyTo;
        Payload = packet;
    }
}

[Union(0, typeof(Message))]
[Union(1, typeof(Reply))]
public interface IMessage
{
    ulong Id { get; }
    public object? Payload { get; set; }
}
