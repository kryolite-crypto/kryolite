using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class Message : IMessage
{
    public ulong Id { get; set; }

    public IPacket? Payload { get; set; }

    [MemoryPackConstructor]
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

[MemoryPackable]
public partial class Reply : IMessage
{
    public ulong Id { get; set; }

    public IPacket? Payload { get; set; }

    public ulong ReplyTo { get; set; }

    [MemoryPackConstructor]
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

[MemoryPackable]
[MemoryPackUnion(0, typeof(Message))]
[MemoryPackUnion(1, typeof(Reply))]
public partial interface IMessage
{
    ulong Id { get; }
    public IPacket? Payload { get; set; }
}
