using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class PendingRequest : IPacket
{
    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BlockRequest>>();

        logger.LogInformation("PendingRequest from {hostname}", peer.Uri.ToHostname());

        await peer.ReplyAsync(args.Message.Id, new PendingResponse(
            blockchainManager.GetPendingBlocks().ToList(),
            blockchainManager.GetPendingVotes().ToList(),
            blockchainManager.GetPendingTransactions().Select(x => new TransactionDto(x)).ToList()));
    }
}
