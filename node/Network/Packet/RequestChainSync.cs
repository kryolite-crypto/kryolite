using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class RequestChainSync : IPacket
{
    [Key(0)]
    public SHA256Hash? LastHash { get; init; }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<BlockchainManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RequestChainSync>>();

        logger.LogInformation($"Chain sync requested by {peer.Uri.ToHostname()}");

        var chain = new ChainData();

        if (LastHash is null) {
            chain.Transactions = blockchainManager.GetTransactionsAfterHeight(0, 1_000)
                .Select(x => new Shared.Dto.TransactionDto(x))
                .ToList();

            goto answer;
        }

        var view = blockchainManager.GetView(LastHash);

        if (view is null) {
            chain.Transactions = blockchainManager.GetTransactionsAfterHeight(0, 1_000)
                .Select(x => new Shared.Dto.TransactionDto(x))
                .ToList();

            goto answer;
        }

        chain.Transactions = blockchainManager.GetTransactionsAfterHeight(view.Height ?? 0, 1_000)
            .Select(x => new Shared.Dto.TransactionDto(x))
            .ToList();

        answer:
        _ = peer.SendAsync(chain);
    }
}
