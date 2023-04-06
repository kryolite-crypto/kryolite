using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class Token : TokenBase
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public SHA256Hash TokenId { get; set; } = new();
    public bool IsConsumed { get; set; }
    public LedgerWallet Wallet { get; set; } = new();
    public Contract Contract { get; set; } = new();
}

public class TokenBase
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
