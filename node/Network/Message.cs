using MessagePack;
using MessagePack.Formatters;

namespace Marccacoin;

[MessagePackObject]
public class Message
{
    [Key(0)]
    public Guid Id { get; set; } = Guid.NewGuid();
    [Key(1)]
    public Guid? NodeId { get; set; }
    [Key(2)]
    [MessagePackFormatter(typeof(TypelessFormatter))]
    public object? Payload { get; set; }
}
