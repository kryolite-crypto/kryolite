using System.Threading.Tasks.Dataflow;
using Kryolite.Node.Services;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class ChainData : IPacket
{
    [Key(0)]
    public List<TransactionDto>? Transactions { get; set; }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<BlockchainManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ChainData>>();
        var syncService = scope.ServiceProvider.GetRequiredService<IBufferService<Chain>>();

        logger.LogInformation($"Received blockchain from {peer.Uri.ToHostname()}");

        if (Transactions is not null && !ChainObserver.InProgress)
        {
            var chain = new Chain(peer, Transactions);
            syncService.Add(chain);
        }
    }
}
