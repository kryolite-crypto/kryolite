using MessagePack;
using MessagePack.Formatters;

namespace Kryolite.Shared;

[MessagePackObject]
public class TransactionPayload
{
    [Key(0)]
    [MessagePackFormatter(typeof(PacketFormatter))]
    public object? Payload { get; set; }
}
