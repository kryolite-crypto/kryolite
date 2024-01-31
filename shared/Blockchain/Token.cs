using System.Text.Json.Serialization;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class Token : TokenBase
{
    public ulong Id { get; set; }
    public SHA256Hash TokenId { get; set; } = new();
    public bool IsConsumed { get; set; }
    public Address Ledger { get; set; } = new();
    public Address Contract { get; set; } = new();
}

public class TokenBase
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
