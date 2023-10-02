using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class ViewBroadcast : IPacket
{
    [Key(0)]
    public SHA256Hash ViewHash;
    [Key(1)]
    public SHA256Hash LastHash;
    [Key(2)]
    public BigInteger Weight;

    public ViewBroadcast(SHA256Hash viewHash, SHA256Hash lastHash, BigInteger weight)
    {
        ViewHash = viewHash;
        LastHash = lastHash;
        Weight = weight;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        if (peer.IsSyncInProgress)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        logger.LogDebug($"Received ViewBroadcast from {peer.Uri.ToHostname()}");

        var chainState = storeManager.GetChainState();

        if (LastHash != chainState.LastHash)
        {
            if (Weight > chainState.Weight)
            {
                // REQUEST SYNC
            }

            return;
        }

        var result = await peer.PostAsync(new ViewRequestByHash(ViewHash));

        if (result is null || result.Payload is not ViewResponse response || response.View is null)
        {
            return;
        }

        if (storeManager.AddView(response.View, false, true))
        {
            args.Rebroadcast = true;
        }
    }
}
