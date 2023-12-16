using System.Numerics;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeInfoResponse : IPacket
{
    [Key(0)]
    public long CurrentTime { get; init; }
    [Key(1)]
    public long Height { get; init; }
    [Key(2)]
    public BigInteger Weight { get; init; }
    [Key(3)]
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoResponse>>();
        var syncService = scope.ServiceProvider.GetRequiredService<IBufferService<Chain, SyncService>>();

        logger.LogInformation("NodeInfoResponse from {hostname}", peer.Uri.ToHostname());

        if (peer.IsSyncInProgress)
        {
            // Do not request chain sync if previous sync is ongoing
            logger.LogDebug("Sync in progress {hostname}", peer.Uri.ToHostname());
            return;
        }

        if (peer.IsForked && blockchainManager.GetView(LastHash) is null)
        {
            // do not sync from peers marked as forked
            logger.LogDebug("Ignore peer due to fork {hostname}", peer.Uri.ToHostname());
            return;
        }

        peer.IsForked = false;

        var chainState = blockchainManager.GetChainState();

        if (LastHash != chainState.ViewHash)
        {
            if (Weight <= chainState.Weight)
            {
                // Remote is behind, do nothing
                // TODO: Should we advertise our own Weight here?
                logger.LogDebug("Remote node is behind");
                return;
            }

            logger.LogInformation("{hostname}: View ({lastHash}) at height {height} does not match with local view ({viewHash}) and remote weight ({remoteWeight}) is higher compared to local ({localWeight}). Performing sync...",
                peer.Uri.ToHostname(),
                LastHash,
                Height,
                chainState.ViewHash,
                Weight,
                chainState.Weight
            );

            syncService.Add(new Chain(peer, Height));
            return;
        }

        var result = await peer.PostAsync(new PendingRequest());

        if (result is null || result.Payload is not PendingResponse response)
        {
            logger.LogDebug("Invalid PendingResponse");
            return;
        }

        foreach (var block in response.Blocks)
        {
            if (blockchainManager.BlockExists(block.GetHash()))
            {
                continue;
            }

            if (!blockchainManager.AddBlock(block, true))
            {
                logger.LogDebug("Failed to apply pending block");
                return;
            }
        }

        foreach (var vote in response.Votes)
        {
            if (blockchainManager.VoteExists(vote.GetHash()))
            {
                continue;
            }

            if (!blockchainManager.AddVote(vote, true))
            {
                logger.LogDebug("Failed to apply pending vote");
                return;
            }
        }

        foreach (var tx in response.Transactions)
        {
            if (blockchainManager.TransactionExists(tx.CalculateHash()))
            {
                continue;
            }

            var exc = blockchainManager.AddTransaction(tx, true);

            if (exc != ExecutionResult.SUCCESS)
            {
                logger.LogDebug("Failed to apply pending transaction");
                return;
            }
        }
    }
}
