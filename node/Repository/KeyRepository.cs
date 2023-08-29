using Kryolite.Shared;
using MessagePack;
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
            var keys = Wallet.Create();
            var bytes = MessagePackSerializer.Serialize(keys);

            File.WriteAllBytes(StorePath, bytes);
        }
    }

    public Wallet GetKey()
    {
        var bytes = File.ReadAllBytes(StorePath);
        return MessagePackSerializer.Deserialize<Wallet>(bytes);
    }
}
