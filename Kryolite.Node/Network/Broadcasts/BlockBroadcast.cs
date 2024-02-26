using Kryolite.Grpc.NodeService;
using Kryolite.Shared;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

[MemoryPackable]
public partial class BlockBroadcast : IBroadcast
{
    public SHA256Hash Blockhash { get; set; }

    [MemoryPackConstructor]
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

        var client = connManager.CreateClient<INodeService>(node);
        var block = client.GetBlock(Blockhash);

        if (block is not null)
        {
            storeManager.AddBlock(block, true);
        }

        return Task.CompletedTask;
    }
}
