using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionData : IPacket
{
    [Key(0)]
    public IList<Transaction> Transactions { get; set; } = new List<Transaction>();

    public Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        if (Transactions.Count == 0) {
            return Task.CompletedTask;
        }

        context.Logger.LogInformation($"Received {Transactions.Count} transactions from {args.Message.NodeId}");

        var valid = context.BlockchainManager.AddTransactionsToQueue(Transactions);

        args.Message.Payload = new TransactionData
        {
            Transactions = valid
        };

        if (valid.Count > 0)
        {
            args.Rebroadcast = true;
        }

        return Task.CompletedTask;
    }
}
