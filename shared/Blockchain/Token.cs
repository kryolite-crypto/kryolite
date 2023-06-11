using MessagePack;
using System.Data.Common;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

[MessagePackObject]
public class Token : TokenBase
{
    [Key(0)]
    public SHA256Hash TokenId { get; set; } = new();
    [Key(1)]
    public bool IsConsumed { get; set; }
    [Key(2)]
    public Address Ledger { get; set; } = new();
    [Key(3)]
    public Address Contract { get; set; } = new();

    public static Token Read(DbDataReader reader)
    {
        return new Token
        {
            TokenId = reader.GetString(0),
            IsConsumed = reader.GetBoolean(1),
            Ledger = reader.GetString(2),
            Contract = reader.GetString(3)
        };
    }
}

public class TokenBase
{
    [Key(4)]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [Key(5)]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
