using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionBatch : IPacket
{
    [Key(0)]
    public List<TransactionDto> Transactions { get; set; } = new List<TransactionDto>();

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        if (Transactions.Count == 0)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var txQueue = scope.ServiceProvider.GetRequiredService<IBufferService<TransactionDto, IncomingTransactionService>>();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBatch>>();

        logger.LogInformation($"Received {Transactions.Count} transactions from {peer.Uri.ToHostname()}");

        var keys = Transactions.AsParallel()
            .Select(x => x.CalculateHash())
            .ToHashSet();

        if (keys is null)
        {
            logger.LogError($"Null chain received from {peer.Uri.ToHostname()}");
            return;
        }

        foreach (var tx in Transactions)
        {
            foreach (var parent in tx.Parents)
            {
                if (!keys.Contains(parent) && !storeManager.Exists(parent))
                {
                    logger.LogDebug($"Received transaction with unknown parent reference ({parent}), requesting node info");

                    var request = new QueryNodeInfo();

                    _ = peer.SendAsync(request);
                    return;

                }
            }
        }

        _ = txQueue.AddAsync(Transactions);
        args.Rebroadcast = true;
    }
}
