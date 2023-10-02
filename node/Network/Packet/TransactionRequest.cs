using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionRequest : IPacket
{
    [Key(0)]
    public SHA256Hash TransactionId { get; }

    public TransactionRequest(SHA256Hash transactionId)
    {
        TransactionId = transactionId;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoRequest>>();

        logger.LogDebug($"Received TransactionRequest from {peer.Uri.ToHostname()}");

        var tx = blockchainManager.GetTransactionForHash(TransactionId);

        if (tx is null)
        {
            await peer.ReplyAsync(args.Message.Id, new TransactionResponse(null));
            return;
        }

        await peer.ReplyAsync(args.Message.Id, new TransactionResponse(new TransactionDto(tx)));
    }
}
