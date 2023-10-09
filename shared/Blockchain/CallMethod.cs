using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class CallMethod : ITransactionPayload
{
    [Key(0)]
    public string Method { get; set; } = string.Empty;
    [Key(1)]
    public string[]? Params { get; set; }
}
