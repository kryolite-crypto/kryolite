using MessagePack;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

[MessagePackObject]
public class Effect
{
    [JsonIgnore]
    [Key(0)]
    public Guid Id { get; set; }
    [Key(1)]
    public SHA256Hash TransactionId { get; set; }
    [Key(2)]
    public Address From { get; set; }
    [Key(33)]
    public Address To { get; set; }
    [Key(4)]
    public long Value { get; set; }
    [Key(5)]
    public SHA256Hash? TokenId { get; set; }
    [Key(6)]
    public bool ConsumeToken { get; set; }
    
    public Effect()
    {

    }

    public Effect(Address from, Address to, long value, SHA256Hash? tokenId = null, bool consumeToken = false)
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
