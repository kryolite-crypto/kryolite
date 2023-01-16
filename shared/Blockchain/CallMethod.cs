using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class CallMethod
{
    [Key(0)]
    public string Method { get; set; } = string.Empty;
}
