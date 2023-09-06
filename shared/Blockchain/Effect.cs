using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Effect
{
    [Key(0)]
    public Address From { get; set; }
    [Key(1)]
    public Address To { get; set; }
    [Key(2)]
    public long Value { get; set; }
    [Key(3)]
    public Address? Contract { get; set; }
    [Key(4)]
    public SHA256Hash? TokenId { get; set; }
    [Key(5)]
    public bool ConsumeToken { get; set; }
    
    public Effect()
    {
        From = Address.NULL_ADDRESS;
        To = Address.NULL_ADDRESS;
    }

    public Effect(Address contract, Address from, Address to, long value, SHA256Hash? tokenId = null, bool consumeToken = false)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Value = value;
        TokenId = tokenId;
        ConsumeToken = consumeToken;
    }

    public bool IsTokenEffect()
    {
        return TokenId is not null;
    }
}
