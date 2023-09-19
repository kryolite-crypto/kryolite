using Kryolite.Shared;
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
        if (peer.LastChainSync is not null && (DateTime.Now - peer.LastChainSync).Value.TotalSeconds < 10)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RequestChainSync>>();

        logger.LogInformation($"Chain sync requested by {peer.Uri.ToHostname()}");

        var chain = new ChainData();

        if (LastHash is null) {
            chain.Transactions = blockchainManager.GetTransactionsAfterHeight(0)
                .Select(x => new Shared.Dto.TransactionDto(x))
                .ToList();

            goto answer;
        }

        var chainState = blockchainManager.GetChainState();

        if (LastHash == chainState.LastHash)
        {
            var batch = new TransactionBatch
            {
                Transactions = blockchainManager.GetPendingTransactions()
                    .Select(x => new Shared.Dto.TransactionDto(x))
                    .ToList()
            };

            _ = peer.SendAsync(chain);
        }

        var view = blockchainManager.GetView(LastHash);

        if (view is null) {
            chain.Transactions = blockchainManager.GetTransactionsAfterHeight(0)
                .Select(x => new Shared.Dto.TransactionDto(x))
                .ToList();

            goto answer;
        }

        chain.Transactions = blockchainManager.GetTransactionsAfterHeight(view.Height ?? 0)
            .Select(x => new Shared.Dto.TransactionDto(x))
            .ToList();

answer:
        peer.LastChainSync = DateTime.Now;

        _ = peer.SendAsync(chain);
    }
}
