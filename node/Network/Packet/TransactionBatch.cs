using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
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

        var toAdd = new List<TransactionDto>();

        foreach (var tx in Transactions)
        {
            bool missingParents = false;

            foreach (var parent in tx.Parents)
            {
                if (!keys.Contains(parent) && !storeManager.Exists(parent))
                {
                    missingParents = true;
                    break;
                }
            }

            if (missingParents)
            {
                peer.QueuedTransactions.TryAdd(tx.CalculateHash(), (DateTimeOffset.Now.AddMinutes(2), tx));
                continue;
            }

            toAdd.Add(tx);
        }

        foreach (var entry in peer.QueuedTransactions)
        {
            var (_, tx) = entry.Value;

            bool missingParents = false;

            foreach (var parent in tx.Parents)
            {
                if (!keys.Contains(parent) && !storeManager.Exists(parent))
                {
                    missingParents = true;
                    break;
                }
            }

            if (!missingParents)
            {
                keys.Add(tx.CalculateHash());
                toAdd.Add(tx);
            }
        }

        if (toAdd.Count == 0)
        {
            return;
        }

        storeManager.AddTransactionBatch(toAdd, false);

        // do not rebroadcast invalid transactions
        Transactions = toAdd.Where(x => x.IsValid)
            .ToList();

        if (Transactions.Count > 0)
        {
            args.Rebroadcast = true;
        }
    }
}
