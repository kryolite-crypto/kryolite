using System.Numerics;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

[MemoryPackable]
public partial class ViewBroadcast : IBroadcast
{
    public SHA256Hash ViewHash { get; set; }
    public SHA256Hash LastHash { get; set; }
    public byte[] WeightBytes { get; set; }

    [MemoryPackConstructor]
    public ViewBroadcast(SHA256Hash viewHash, SHA256Hash lastHash, byte[] weightBytes)
    {
        ViewHash = viewHash;
        LastHash = lastHash;
        WeightBytes = weightBytes;
    }

    public ViewBroadcast(SHA256Hash viewHash, SHA256Hash lastHash, BigInteger weight)
    {
        ViewHash = viewHash;
        LastHash = lastHash;
        WeightBytes = weight.ToByteArray();
    }

    public Task Handle(Node node, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring ViewBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        if (node.IsForked && storeManager.GetView(LastHash) is null)
        {
            logger.LogDebug("Ignoring ViewBroadcast, node has fork");
            return Task.CompletedTask;
        }

        node.IsForked = false;

        logger.LogDebug("Received ViewBroadcast from {hostname}", node.Uri.ToHostname());

        var chainState = storeManager.GetChainState();

        if (chainState.ViewHash == ViewHash)
        {
            logger.LogDebug("Already have view {hash}", ViewHash);
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient<INodeService>(node);

        var weight = new BigInteger(WeightBytes);

        // Check that this view extends our current view
        if (LastHash != chainState.ViewHash)
        {
            logger.LogDebug("Weight: {weight}", weight);
            logger.LogDebug("Chainstate: {weight}", chainState.Weight);
            if (weight > chainState.Weight)
            {
                logger.LogDebug("[{hostname}] Has more weight. Request sync", node.Uri.ToHostname());
                SyncManager.AddToQueue(node);
            }
            else if (weight < chainState.Weight)
            {
                var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
                var pubKey = keyRepo.GetKey().PublicKey;

                logger.LogDebug("[{hostname}] Has lower weight. Broadcast our current view", node.Uri.ToHostname());
                client.SuggestView(pubKey, chainState.ViewHash, chainState.Weight);
            }

            return Task.CompletedTask;
        }

        logger.LogDebug("Download view {} from {hostname}", ViewHash, node.Uri.ToHostname());

        var view = client.GetViewForHash(ViewHash);

        if (view is null)
        {
            logger.LogInformation("[{hostname}] ViewRequest failed", node.Uri.ToHostname());
            SyncManager.AddToQueue(node);
            return Task.CompletedTask;
        }

        chainState = storeManager.GetChainState();

        if (chainState.ViewHash != view.LastHash)
        {
            logger.LogDebug("Discarding view {id} from {hostname}, due to invalid LastHash", view.Id, node.Uri.ToHostname());
            return Task.CompletedTask;
        }

        foreach (var hash in view.Blocks)
        {
            if (storeManager.BlockExists(hash))
            {
                continue;
            }

            var block = client.GetBlock(hash);

            if (block is not null)
            {
                storeManager.AddBlock(block, true);
            }
        }

        foreach (var hash in view.Votes)
        {
            if (storeManager.VoteExists(hash))
            {
                continue;
            }

            var vote = client.GetVote(hash);

            if (vote is not null)
            {
                storeManager.AddVote(vote, true);
            }
        }

        foreach (var hash in view.Transactions)
        {
            if (storeManager.TransactionExists(hash))
            {
                continue;
            }

            var tx = client.GetTransaction(hash);

            if (tx is not null)
            {
                storeManager.AddTransaction(tx, true);
            }
        }

        storeManager.AddView(view, true, true);

        return Task.CompletedTask;
    }
}
