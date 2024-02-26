using Kryolite.Shared;
using MemoryPack;
using NBip32Fast;

namespace Kryolite.Wallet;

[MemoryPackable]
public partial class Account
{
    public uint Id { get; set; }
    public Address Address { get; set; }
    public PublicKey PublicKey { get; init; }
    public string? Description { get; set; }

    [MemoryPackConstructor]
    public Account(Address address, PublicKey publicKey, string description)
    {
        Address = address;
        PublicKey = publicKey;
        Description = description;
    }

    public Account(HdKey master, uint id)
    {
        var key = Derivation.Ed25519.Derive(master, new KeyPathElement(id, true));
        var pubKey = Derivation.Ed25519.GetPublic(key.PrivateKey);

        Id = id;
        PublicKey = pubKey;
        Address = PublicKey.ToAddress();
    }
}
