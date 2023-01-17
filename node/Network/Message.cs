using Kryolite.Shared;
using MessagePack;

namespace Kryolite;

[MessagePackObject]
public class Message
{
    [Key(0)]
    public Guid Id { get; set; } = Guid.NewGuid();
    [Key(1)]
    public Guid NodeId { get; set; }
    [Key(2)]
    [MessagePackFormatter(typeof(PacketFormatter))]
    public object? Payload { get; set; }
}
