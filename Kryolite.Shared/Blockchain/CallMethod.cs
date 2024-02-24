
using System.Text.Json.Serialization;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class CallMethod : ITransactionPayload
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public string[]? Params { get; set; }
}
