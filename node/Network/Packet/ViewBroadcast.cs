using System.Numerics;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class ViewBroadcast : IPacket
{
    [Key(0)]
    public SHA256Hash ViewHash { get; set; }
    [Key(1)]
    public SHA256Hash LastHash { get; set; }
    [Key(2)]
    public BigInteger Weight { get; set; }

    public ViewBroadcast(SHA256Hash viewHash, SHA256Hash lastHash, BigInteger weight)
    {
        ViewHash = viewHash;
        LastHash = lastHash;
        Weight = weight;
    }

    public async void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var storeManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TransactionBroadcast>>();

        if (peer.IsSyncInProgress)
        {
            logger.LogDebug("Ignoring ViewBroadcast, sync in progress");
            return;
        }

        if (peer.IsForked && storeManager.GetView(LastHash) is null)
        {
            logger.LogDebug("Ignoring ViewBroadcast, peer has fork");
            return;
        }

        peer.IsForked = false;

        logger.LogDebug("Received ViewBroadcast from {hostname}", peer.Uri.ToHostname());

        var chainState = storeManager.GetChainState();

        if (chainState.ViewHash == ViewHash)
        {
            // already have this
            return;
        }

        // Check that this view extends our current view
        if (LastHash != chainState.ViewHash)
        {
            if (Weight > chainState.Weight)
            {
                // REQUEST SYNC
                logger.LogDebug("[{hostname}] Has more weight. Request sync", peer.Uri.ToHostname());
                await peer.SendAsync(new NodeInfoRequest());
            }
            else if (Weight < chainState.Weight)
            {
                logger.LogDebug("[{hostname}] Has lower weight. Broadcast our current view", peer.Uri.ToHostname());
                var view = storeManager.GetLastView();

                if (view is not null)
                {
                    await peer.SendAsync(new ViewBroadcast(chainState.ViewHash, view.LastHash, chainState.Weight));
                }
            }

            return;
        }

        var result = await peer.PostAsync(new ViewRequestByHash(ViewHash));

        if (result is null || result.Payload is not ViewResponse response || response.View is null)
        {
            logger.LogInformation("[{hostname}] ViewRequest failed", peer.Uri.ToHostname());
            await peer.SendAsync(new NodeInfoRequest());
            return;
        }

        new ViewResponse(response.View)
            .Handle(peer, args, serviceProvider);
    }
}
