using DuckDB.NET.Data;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class Token : TokenBase
{
    public SHA256Hash TokenId { get; set; } = new();
    public bool IsConsumed { get; set; }
    public Address Ledger { get; set; } = new();
    public Address Contract { get; set; } = new();

    public static Token Read(DuckDBDataReader reader)
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
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
