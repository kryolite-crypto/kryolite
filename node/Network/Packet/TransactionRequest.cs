using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MemoryPackable]
public partial class TransactionRequest : IPacket
{
    public SHA256Hash TransactionId { get; set; }

    [MemoryPackConstructor]
    public TransactionRequest(SHA256Hash transactionId)
    {
        TransactionId = transactionId;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionRequest>>();

        logger.LogDebug("Received TransactionRequest from {hostname}", peer.Uri.ToHostname());

        var tx = blockchainManager.GetTransactionForHash(TransactionId);

        if (tx is null)
        {
            await peer.ReplyAsync(args.Message.Id, new TransactionResponse(null));
            return;
        }

        await peer.ReplyAsync(args.Message.Id, new TransactionResponse(new TransactionDto(tx)));
    }
}
