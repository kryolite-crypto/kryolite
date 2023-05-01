using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class Effect
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public SHA256Hash TransactionId { get; set; }
    public Address From { get; set; }
    public Address To { get; set; }
    public ulong Value { get; set; }
    public SHA256Hash? TokenId { get; set; }
    public bool ConsumeToken { get; set; }
    
    public Effect(Address from, Address to, ulong value, SHA256Hash? tokenId = null, bool consumeToken = false)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Value = value;
        TokenId = tokenId;
        ConsumeToken = consumeToken;
    }

    public bool IsTokenEffect()
    {
        return TokenId is not null;
    }
}
