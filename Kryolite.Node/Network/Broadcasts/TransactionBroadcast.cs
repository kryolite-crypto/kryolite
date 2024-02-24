using Kryolite.Shared;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

[MemoryPackable]
public partial class TransactionBroadcast : IBroadcast
{
    public SHA256Hash TransactionId { get; set; }

    [MemoryPackConstructor]
    public TransactionBroadcast(SHA256Hash transactionId)
    {
        TransactionId = transactionId;
    }

    public Task Handle(Node node, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring TransactionBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        logger.LogDebug("Received TransactionBroadcast from {hostname}", node.Uri.ToHostname());

        if (storeManager.TransactionExists(TransactionId))
        {
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient<INodeService>(node);
        var tx = client.GetTransaction(TransactionId);

        if (tx is not null)
        {
            storeManager.AddTransaction(tx, true);
        }

        return Task.CompletedTask;
    }
}
