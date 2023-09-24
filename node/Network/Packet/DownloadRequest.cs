using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kryolite.Shared.Dto;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node;

[MessagePackObject]
public class DownloadRequest : IPacket
{
    [Key(0)]
    public long StartHeight { get; set; }
    [Key(1)]
    public long EndHeight { get; set; }

    public DownloadRequest(long startHeight, long endHeight)
    {
        StartHeight = startHeight;
        EndHeight = endHeight;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeBroadcast>>();

        logger.LogDebug($"Received DownloadRequest from {peer.Uri.ToHostname()}");

        var transactions = new List<TransactionDto>();

        for (var i = StartHeight; i <= EndHeight; i++)
        {
            var txs = storeManager.GetTransactionsAtHeight(i);

            if (txs.Count == 0)
            {
                // we are at the tip, add pending transactions to request
                transactions.AddRange(storeManager.GetPendingTransactions().Select(x => new TransactionDto(x)));
                break;
            }

            transactions.AddRange(txs
                .Where(x => x.ExecutionResult != ExecutionResult.STALE && x.ExecutionResult != ExecutionResult.ORPHAN)
                .Select(x => new TransactionDto(x)));
        }

        if (transactions.Count == 0)
        {
            return;
        }

        _ = peer.ReplyAsync(args.Message.Id, new DownloadResponse(transactions));
    }
}
