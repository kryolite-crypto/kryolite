using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class BlockBroadcast : IPacket
{
    [Key(0)]
    public SHA256Hash Blockhash { get; set; }

    public BlockBroadcast(SHA256Hash blockhash)
    {
        Blockhash = blockhash;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (peer.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring BlockBroadcast, sync in progress");
            return;
        }

        logger.LogDebug("Received BlockBroadcast from {hostname}", peer.Uri.ToHostname());

        if (storeManager.BlockExists(Blockhash))
        {
            return;
        }

        var result = await peer.PostAsync(new BlockRequest(Blockhash));

        if (result is null || result.Payload is not BlockResponse response || response.Block is null)
        {
            return;
        }

        storeManager.AddBlock(response.Block, true);
    }
}
