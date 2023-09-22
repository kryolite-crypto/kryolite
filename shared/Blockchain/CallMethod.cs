using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class CallMethod : ITransactionPayload
{
    [Key(0)]
    public string Method { get; set; } = string.Empty;
    [Key(1)]
    public object[]? Params { get; set; }
}
