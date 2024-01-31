using System.Data.Common;
using System.Text.Json.Serialization;
using MemoryPack;
using NSec.Cryptography;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class Wallet
{
    public Address Address { get; set; } = Address.NULL_ADDRESS;
    public string? Description { get; set; }
    public PublicKey PublicKey { get; set; } = PublicKey.NULL_PUBLIC_KEY;
    public PrivateKey PrivateKey { get; set; } = new PrivateKey();

    [MemoryPackConstructor]
    public Wallet()
    {

    }

    public static Wallet Create()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = NSec.Cryptography.Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving });
        var pubKey = key.Export(KeyBlobFormat.RawPublicKey);
        var privKey = key.Export(KeyBlobFormat.RawPrivateKey);

        var wallet = new Wallet
        {
            PublicKey = pubKey,
            PrivateKey = privKey
        };

        wallet.Address = wallet.PublicKey.ToAddress();

        return wallet;
    }
}
