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
    [Key(1)]
    public List<Block> Blocks { get; set; }
    [Key(2)]
    public List<Vote> Votes { get; set; }
    [Key(3)]
    public List<TransactionDto> Transactions { get; set; }

    public ViewResponse(View? view)
    {
        View = view;
        Blocks = new();
        Votes = new();
        Transactions = new();
    }

    public ViewResponse(View? view, List<Block> blocks, List<Vote> votes, List<TransactionDto> transactions)
    {
        View = view;
        Blocks = blocks;
        Votes = votes;
        Transactions = transactions;
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

        if (chainState.ViewHash != View.LastHash)
        {
            logger.LogDebug("Discarding view {id} from {hostname}, due to invalid LastHash", View.Id, peer.Uri.ToHostname());
            return;
        }

        foreach (var blockhash in View.Blocks)
        {
            if (!storeManager.BlockExists(blockhash))
            {
                var blockResult = await peer.PostAsync(new BlockRequest(blockhash));

                if (blockResult is null || blockResult.Payload is not BlockResponse blockResponse || blockResponse.Block is null)
                {
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                if (!storeManager.AddBlock(blockResponse.Block, true))
                {
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
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                if (!storeManager.AddVote(voteResponse.Vote, true))
                {
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
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                var exr = storeManager.AddTransaction(txResponse.Transaction, true);

                if (exr != ExecutionResult.SUCCESS || exr != ExecutionResult.SCHEDULED)
                {
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }
            }
        }

        if (!storeManager.AddView(View, true, true))
        {
            logger.LogDebug("[{hostname}] Failed to apply view", peer.Uri.ToHostname());
        }
    }
}

[MessagePackObject]
public class ViewRangeResponse : IPacket
{
    [Key(0)]
    public List<ViewResponse> Views { get; set; } = new();

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
