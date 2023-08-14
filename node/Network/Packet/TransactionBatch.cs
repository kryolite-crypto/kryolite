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
                    logger.LogInformation($"Received transaction with unknown parent reference ({parent}), requesting sync");

                    var lastHash = storeManager.GetChainState().LastHash ?? new SHA256Hash();
                    var request = new RequestChainSync
                    {
                        LastHash = lastHash
                    };

                    _ = peer.SendAsync(request);
                    return;

                }
            }
        }

        storeManager.AddTransactionBatch(Transactions);
        args.Rebroadcast = true;
    }
}
