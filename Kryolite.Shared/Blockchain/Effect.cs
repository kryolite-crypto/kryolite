using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class Effect
{
    public Address From { get; set; }
    public Address To { get; set; }
    public ulong Value { get; set; }
    public Address Contract { get; set; }
    public SHA256Hash? TokenId { get; set; }
    public bool ConsumeToken { get; set; }

    [MemoryPackConstructor]    
    public Effect()
    {
        From = Address.NULL_ADDRESS;
        To = Address.NULL_ADDRESS;
        Contract = Address.NULL_ADDRESS;
    }

    public Effect(Address contract, Address from, Address to, ulong value, SHA256Hash? tokenId = null, bool consumeToken = false)
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
