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

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBatch>>();

        logger.LogInformation($"Received {Transactions.Count} transactions from {peer.Uri.ToHostname()}");

        blockchainManager.AddTransactionBatch(Transactions);
    }
}
