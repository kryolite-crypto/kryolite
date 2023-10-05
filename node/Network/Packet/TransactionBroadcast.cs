using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionBroadcast : IPacket
{
    [Key(0)]
    public SHA256Hash TransactionId { get; set; }

    public TransactionBroadcast(SHA256Hash transactionId)
    {
        TransactionId = transactionId;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (peer.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring TransactionBroadcast, sync in progress");
            return;
        }

        logger.LogDebug($"Received TransactionBroadcast from {peer.Uri.ToHostname()}");

        if (storeManager.TransactionExists(TransactionId))
        {
            return;
        }

        var result = await peer.PostAsync(new TransactionRequest(TransactionId));

        if (result is null || result.Payload is not TransactionResponse response || response.Transaction is null)
        {
            return;
        }

        var er = storeManager.AddTransaction(response.Transaction, false);

        if (er == ExecutionResult.SUCCESS)
        {
            args.Rebroadcast = true;
        }
    }
}
