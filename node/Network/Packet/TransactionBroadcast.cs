using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionBroadcast : IPacket
{
    [Key(0)]
    public List<TransactionDto> Transactions { get; set; } = new List<TransactionDto>();

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        if (Transactions.Count == 0)
        {
            return;
        }

        if (peer.IsSyncInProgress)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        logger.LogInformation($"Received {Transactions.Count} transactions from {peer.Uri.ToHostname()}");

        var keys = Transactions.AsParallel()
            .Select(x => x.CalculateHash())
            .ToHashSet();

        if (keys is null)
        {
            logger.LogError($"Null chain received from {peer.Uri.ToHostname()}");
            return;
        }

        const int SEARCH_DEPTH = 100;

        var missing = new HashSet<SHA256Hash>();

        foreach (var tx in Transactions)
        {
            foreach (var parent in tx.Parents)
            {
                if (!keys.Contains(parent) && !storeManager.Exists(parent))
                {
                    missing.Add(parent);
                }
            }
        }

        var chainState = storeManager.GetChainState();

        for (int i = 0; i < SEARCH_DEPTH; i++)
        {
            var batch = missing.ToList(); // Copy current working set
            missing.Clear();

            foreach (var hash in batch)
            {
                var reply = await peer.PostAsync(new TransactionRequest(hash));

                if (reply is null || reply.Payload is not TransactionResponse response || response.Transaction is null)
                {
                    await peer.SendAsync(new NodeInfoRequest());
                    return;
                }

                var tx = response.Transaction;

                foreach (var parent in tx.Parents)
                {
                    if (!keys.Contains(parent) && !storeManager.Exists(parent))
                    {
                        missing.Add(parent);
                    }
                }

                // If we receive view below local chain we need to resync
                if (tx.TransactionType == TransactionType.VIEW)
                {
                    var height = BitConverter.ToInt64(tx.Data);

                    if (height <= chainState.Height)
                    {
                        await peer.SendAsync(new NodeInfoRequest());
                        return;
                    }
                }

                keys.Add(response.Transaction.CalculateHash());
                Transactions.Add(response.Transaction);
            }

            if (missing.Count == 0)
            {
                break;
            }
        }

        if (missing.Count > 0)
        {
            await peer.SendAsync(new NodeInfoRequest());
            return;
        }

        if (!storeManager.AddTransactionBatch(Transactions, false))
        {
            await peer.SendAsync(new NodeInfoRequest());
            return;
        }

        // do not rebroadcast invalid transactions
        Transactions = Transactions.Where(x => x.IsValid)
            .ToList();

        if (Transactions.Count > 0)
        {
            args.Rebroadcast = true;
        }
    }
}
