using MessagePack;
using System.Data.Common;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

[MessagePackObject]
public class Token : TokenBase
{
    [Key(0)]
    public ulong Id { get; set; }
    [Key(1)]
    public SHA256Hash TokenId { get; set; } = new();
    [Key(2)]
    public bool IsConsumed { get; set; }
    [Key(3)]
    public Address Ledger { get; set; } = new();
    [Key(4)]
    public Address Contract { get; set; } = new();
}

public class TokenBase
{
    [Key(5)]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [Key(6)]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
