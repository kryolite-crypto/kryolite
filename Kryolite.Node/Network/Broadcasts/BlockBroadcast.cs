using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class BlockBroadcast : IBroadcast
{
    public SHA256Hash Blockhash;

    public BlockBroadcast()
    {
        Blockhash = new();
    }

    public BlockBroadcast(SHA256Hash blockhash)
    {
        Blockhash = blockhash;
    }

    public Task Handle(Node node, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring BlockBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        logger.LogDebug("Received BlockBroadcast from {hostname}", node.Uri.ToHostname());

        if (storeManager.BlockExists(Blockhash))
        {
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient(node);
        var block = client.GetBlock(Blockhash);

        if (block is not null)
        {
            storeManager.AddBlock(block, true);
        }

        return Task.CompletedTask;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.BLOCK_BROADCAST;
    }

    public int GetLength()
    {
        return SHA256Hash.HASH_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Blockhash);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Blockhash.Buffer, SHA256Hash.HASH_SZ);
    }
}
