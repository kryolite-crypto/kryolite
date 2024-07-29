using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Interface;
using Kryolite.Shared;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class TransactionBroadcast : IBroadcast
{
    public SHA256Hash TransactionId;

    public TransactionBroadcast()
    {
        TransactionId = new();
    }

    public TransactionBroadcast(SHA256Hash transactionId)
    {
        TransactionId = transactionId;
    }

    public Task Handle(NodeConnection connection, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (connection.Node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring TransactionBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        logger.LogDebug("Received TransactionBroadcast from {hostname}", connection.Node.Uri.ToHostname());

        if (storeManager.TransactionExists(TransactionId))
        {
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient(connection);
        var tx = client.GetTransaction(TransactionId);

        if (tx is not null)
        {
            storeManager.AddTransaction(tx, true);
        }

        return Task.CompletedTask;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.TRANSACTION_BROADCAST;
    }

    public int GetLength()
    {
        return SHA256Hash.HASH_SZ;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(TransactionId);
    }

    public void Deserialize(ref Serializer serializer)
    {
        var buffer = new byte[SHA256Hash.HASH_SZ];
        serializer.Read(ref buffer, SHA256Hash.HASH_SZ);
        TransactionId = new SHA256Hash(buffer);
    }
}
