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

    public static Wallet Read(DbDataReader reader)
    {
        var wallet = new Wallet();

        wallet.Address = reader.GetString(0);
        if (!reader.IsDBNull(1))
        {
            wallet.Description = reader.GetString(1);
        }
        wallet.PublicKey = reader.GetString(2);

        var t = reader.GetFieldType(3);
        using var ms = new MemoryStream();
        reader.GetStream(3).CopyTo(ms);
        wallet.PrivateKey = ms.ToArray();

        return wallet;
    }
}
