using Kryolite.Shared;
using Microsoft.Extensions.Configuration;

namespace Kryolite.Node.Repository;

public class KeyRepository : IKeyRepository
{
    private string StorePath { get; }

    public KeyRepository(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("data-dir") ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        StorePath = Path.Combine(dataDir, "node.key");

        if (!Path.Exists(StorePath))
        {
            var wallet = Wallet.Wallet.CreateFromRandomSeed();
            wallet.CreateAccount();
            var bytes = Serializer.Serialize<Wallet.Wallet>(wallet);

            File.WriteAllBytes(StorePath, bytes);
        }
    }

    public PublicKey GetPublicKey()
    {
        var bytes = File.ReadAllBytes(StorePath);
        var wallet = Serializer.Deserialize<Wallet.Wallet>(bytes) ?? throw new Exception("failed to deserialize node key");
        return wallet.Accounts[0].PublicKey;
    }

    public PrivateKey GetPrivateKey()
    {
        var bytes = File.ReadAllBytes(StorePath);
        var wallet = Serializer.Deserialize<Wallet.Wallet>(bytes) ?? throw new Exception("failed to deserialize node key");
        return wallet.GetPrivateKey(wallet.Accounts[0].PublicKey) ?? throw new Exception("node keys not initialized");
    }
}
