using System.Numerics;
using Kryolite.ByteSerializer;
using Kryolite.Grpc.NodeService;
using Kryolite.Node.Repository;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Network;

public class ViewBroadcast : IBroadcast
{
    public SHA256Hash ViewHash;
    public SHA256Hash LastHash;
    public BigInteger Weight;

    public ViewBroadcast()
    {
        ViewHash = new();
        LastHash = new();
    }

    public ViewBroadcast(SHA256Hash viewHash, SHA256Hash lastHash, BigInteger weight)
    {
        ViewHash = viewHash;
        LastHash = lastHash;
        Weight = weight;
    }

    public Task Handle(NodeConnection connection, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var connManager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (connection.Node.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring ViewBroadcast, sync in progress");
            return Task.CompletedTask;
        }

        if (connection.Node.IsForked && storeManager.GetView(LastHash) is null)
        {
            logger.LogDebug("Ignoring ViewBroadcast, node has fork");
            return Task.CompletedTask;
        }

        connection.Node.IsForked = false;

        logger.LogDebug("Received ViewBroadcast from {hostname}", connection.Node.Uri.ToHostname());

        var chainState = storeManager.GetChainState();

        if (chainState.ViewHash == ViewHash)
        {
            logger.LogDebug("Already have view {hash}", ViewHash);
            return Task.CompletedTask;
        }

        var client = connManager.CreateClient(connection);

        // Check that this view extends our current view
        if (LastHash != chainState.ViewHash)
        {
            logger.LogDebug("Weight: {weight}", Weight);
            logger.LogDebug("Chainstate: {weight}", chainState.Weight);
            
            if (Weight > chainState.Weight)
            {
                logger.LogDebug("[{hostname}] Has more weight. Request sync", connection.Node.Uri.ToHostname());
                SyncManager.AddToQueue(connection);
            }
            
            /*else if (Weight < chainState.Weight)
            {
                var keyRepo = scope.ServiceProvider.GetRequiredService<IKeyRepository>();
                var pubKey = keyRepo.GetPublicKey();

                logger.LogDebug("[{hostname}] Has lower weight. Broadcast our current view", connection.Node.Uri.ToHostname());
                client.SuggestView(new SyncRequest(pubKey, chainState.ViewHash, chainState.Weight));
            }*/

            return Task.CompletedTask;
        }

        logger.LogDebug("Download view {} from {hostname}", ViewHash, connection.Node.Uri.ToHostname());

        var view = client.GetViewForHash(ViewHash);

        if (view is null)
        {
            logger.LogInformation("[{hostname}] ViewRequest failed", connection.Node.Uri.ToHostname());
            SyncManager.AddToQueue(connection);
            return Task.CompletedTask;
        }

        chainState = storeManager.GetChainState();

        if (chainState.ViewHash != view.LastHash)
        {
            logger.LogDebug("Discarding view {id} from {hostname}, due to invalid LastHash", view.Id, connection.Node.Uri.ToHostname());
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

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VIEW_BROADCAST;
    }

    public int GetLength() =>
        Serializer.SizeOf(ViewHash) +
        Serializer.SizeOf(LastHash) +
        Serializer.SizeOf(Weight);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(ViewHash);
        serializer.Write(LastHash);
        serializer.Write(Weight);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref ViewHash);
        serializer.Read(ref LastHash);
        serializer.Read(ref Weight);
    }
}
