using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class RequestChainSync : IPacket
{
    [Key(0)]
    public SHA256Hash? LastHash { get; init; }

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Chain sync requested by {peer.Uri.ToHostname()}");

        var chain = new TransactionBatch();

        if (LastHash is null) {
            chain.Transactions = context.BlockchainManager.GetTransactionsAfterHeight(0, 1_000)
                .Select(x => new Shared.Dto.TransactionDto(x))
                .ToList();

            goto answer;
        }

        var view = context.BlockchainManager.GetView(LastHash);

        if (view is null) {
            chain.Transactions = context.BlockchainManager.GetTransactionsAfterHeight(0, 1_000)
                .Select(x => new Shared.Dto.TransactionDto(x))
                .ToList();

            goto answer;
        }

        chain.Transactions = context.BlockchainManager.GetTransactionsAfterHeight(view.Height ?? 0, 1_000)
            .Select(x => new Shared.Dto.TransactionDto(x))
            .ToList();

        answer:
        _ = peer.SendAsync(chain);
    }
}
