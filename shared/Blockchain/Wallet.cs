using System.Data.Common;
using System.Text.Json.Serialization;
using NSec.Cryptography;
using Redbus.Events;

namespace Kryolite.Shared;

public class Wallet : EventBase
{
    public Address Address { get; set; } = new Address();
    public string? Description { get; set; }
    public PublicKey PublicKey { get; set; } = new PublicKey();
    public PrivateKey PrivateKey { get; set; } = new PrivateKey();
    public WalletType WalletType { get; set; }


    private Wallet()
    {

    }

    public static Wallet Create(WalletType walletType)
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
        wallet.WalletType = walletType;

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

        wallet.WalletType = (WalletType)reader.GetByte(4);

        return wallet;
    }
}

public enum WalletType
{
    WALLET,
    VALIDATOR
}
