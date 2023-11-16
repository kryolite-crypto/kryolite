using System.Data.Common;
using System.Text.Json.Serialization;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared;

[MessagePackObject]
public class Wallet
{
    [Key(0)]
    public Address Address { get; set; } = new Address();
    [Key(1)]
    public string? Description { get; set; }
    [Key(2)]
    public PublicKey PublicKey { get; set; } = new PublicKey();
    [Key(3)]
    public PrivateKey PrivateKey { get; set; } = new PrivateKey();


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
            PrivateKey = privKey,
        };

        wallet.Address = wallet.PublicKey.ToAddress();

        return wallet;
    }
}
