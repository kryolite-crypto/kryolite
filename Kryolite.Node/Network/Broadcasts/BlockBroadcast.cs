using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Interface;
using Kryolite.Shared;
using Kryolite.Type;
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

    public Task Handle(NodeConnection connection, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (connection.Node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring BlockBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        logger.LogDebug("Received BlockBroadcast from {hostname}", connection.Node.Uri.ToHostname());

        if (storeManager.BlockExists(Blockhash))
        {
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient(connection);
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
        var buffer = new byte[SHA256Hash.HASH_SZ];
        serializer.Read(ref buffer, SHA256Hash.HASH_SZ);
        Blockhash = new SHA256Hash(buffer);
    }
}
