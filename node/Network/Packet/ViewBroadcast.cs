using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class ViewBroadcast : IPacket
{
    [Key(0)]
    public SHA256Hash ViewHash { get; set; }
    [Key(1)]
    public SHA256Hash LastHash { get; set; }
    [Key(2)]
    public BigInteger Weight { get; set; }

    public ViewBroadcast(SHA256Hash viewHash, SHA256Hash lastHash, BigInteger weight)
    {
        ViewHash = viewHash;
        LastHash = lastHash;
        Weight = weight;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (peer.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring ViewBroadcast, sync in progress");
            return;
        }

        logger.LogDebug($"Received ViewBroadcast from {peer.Uri.ToHostname()}");

        var chainState = storeManager.GetChainState();

        if (chainState.LastHash == ViewHash)
        {
            // already have this
            return;
        }

        if (LastHash != chainState.LastHash)
        {
            if (Weight > chainState.Weight)
            {
                // REQUEST SYNC
                logger.LogInformation($"[{peer.Uri.ToHostname()}] Advertised view has more weight");
                await peer.SendAsync(new NodeInfoRequest());
            }

            return;
        }

        var result = await peer.PostAsync(new ViewRequestByHash(ViewHash));

        if (result is null || result.Payload is not ViewResponse response || response.View is null)
        {
            logger.LogInformation($"[{peer.Uri.ToHostname()}] ViewRequest failed");
            await peer.SendAsync(new NodeInfoRequest());
            return;
        }

        foreach (var blockhash in response.View.Blocks)
        {
            if (!storeManager.BlockExists(blockhash))
            {
                var blockResult = await peer.PostAsync(new BlockRequest(blockhash));

                if (blockResult is null || blockResult.Payload is not BlockResponse blockResponse || blockResponse.Block is null)
                {
                    logger.LogInformation($"[{peer.Uri.ToHostname()}] blockresult");
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                if (!storeManager.AddBlock(blockResponse.Block, true))
                {
                    logger.LogInformation($"[{peer.Uri.ToHostname()}] addblock");
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }
            }
        }

        foreach (var votehash in response.View.Votes)
        {
            if (!storeManager.VoteExists(votehash))
            {
                var voteResult = await peer.PostAsync(new VoteRequest(votehash));

                if (voteResult is null || voteResult.Payload is not VoteResponse voteResponse || voteResponse.Vote is null)
                {
                    logger.LogInformation($"[{peer.Uri.ToHostname()}] voteresult");
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                if (!storeManager.AddVote(voteResponse.Vote, true))
                {
                    logger.LogInformation($"[{peer.Uri.ToHostname()}] addvote");
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }
            }
        }

        foreach (var txid in response.View.Transactions)
        {
            if (!storeManager.TransactionExists(txid))
            {
                var txResult = await peer.PostAsync(new TransactionRequest(txid));

                if (txResult is null || txResult.Payload is not TransactionResponse txResponse || txResponse.Transaction is null)
                {
                    logger.LogInformation($"[{peer.Uri.ToHostname()}] txresult");
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                var exr = storeManager.AddTransaction(txResponse.Transaction, true);

                if (exr != ExecutionResult.SUCCESS)
                {
                    logger.LogInformation($"[{peer.Uri.ToHostname()}] addtransaction");
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }
            }
        }

        if (!storeManager.AddView(response.View, true, true))
        {
            logger.LogInformation($"[{peer.Uri.ToHostname()}] Failed to apply view");
            await peer.SendAsync(new NodeInfoRequest());
            return;
        }
    }
}
