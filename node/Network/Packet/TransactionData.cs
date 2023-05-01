using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionData : IPacket
{
    [Key(0)]
    public IList<Transaction> Transactions { get; set; } = new List<Transaction>();

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        /*if (Transactions.Count == 0) {
            return;
        }

        context.Logger.LogInformation($"Received {Transactions.Count} transactions from {peer.Uri.ToHostname()}");

        var valid = context.BlockchainManager.AddTransactionsToQueue(Transactions);

        args.Message.Payload = new TransactionData
        {
            Transactions = valid
        };

        if (valid.Count > 0)
        {
            args.Rebroadcast = true;
        }*/
    }
}
