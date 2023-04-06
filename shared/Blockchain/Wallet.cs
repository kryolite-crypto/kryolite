using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace Kryolite.Shared;

public class Wallet
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public string? Description { get; set; }
    public string Address { get; set; }
    public PublicKey PublicKey { get; set; }
    public PrivateKey PrivateKey { get; set; }
    public ulong Balance { get; set; }
    public WalletType Type { get; set; }
    public bool Updated { get; set; }

    public List<WalletTransaction> WalletTransactions { get; set; } = new();

    public Wallet()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = NSec.Cryptography.Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextArchiving });
        var pubKey = key.Export(KeyBlobFormat.RawPublicKey);
        var privKey = key.Export(KeyBlobFormat.RawPrivateKey);

        PublicKey = pubKey;
        PrivateKey = privKey;
        Address = PublicKey.ToAddress().ToString();
        Balance = 0;
    }
}

public enum WalletType
{
    WALLET,
    NODE
}
