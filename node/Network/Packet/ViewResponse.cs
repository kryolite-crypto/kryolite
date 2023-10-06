using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class ViewResponse : IPacket
{
    [Key(0)]
    public View? View { get; set; }

    public ViewResponse(View? view)
    {
        View = view;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (View is null)
        {
            return;
        }

        var chainState = storeManager.GetChainState();

        if (chainState.LastHash != View.LastHash)
        {
            logger.LogDebug($"Discarding view {View.Id} from {peer.Uri.ToHostname()}, due to invalid LastHash");
            return;
        }

        foreach (var blockhash in View.Blocks)
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

        foreach (var votehash in View.Votes)
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

        foreach (var txid in View.Transactions)
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

        if (!storeManager.AddView(View, true, true))
        {
            logger.LogDebug($"[{peer.Uri.ToHostname()}] Failed to apply view");
        }
    }
}
