using System.Net.Sockets;
using System.Numerics;
using Kryolite.Node.Services;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeInfoResponse : IPacket
{
    [Key(0)]
    public long CurrentTime { get; init; }
    [Key(1)]
    public long Height { get; init; }
    [Key(2)]
    public BigInteger Weight { get; init; }
    [Key(3)]
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoResponse>>();
        var syncService = scope.ServiceProvider.GetRequiredService<IBufferService<Chain, SyncService>>();

        logger.LogInformation($"NodeInfoResponse from {peer.Uri.ToHostname()}");

        if (peer.IsSyncInProgress)
        {
            // Do not request chain sync if previous sync is ongoing
            return;
        }

        var chainState = blockchainManager.GetChainState();

        if (LastHash != chainState.LastHash && Weight > chainState.Weight)
        {
            logger.LogInformation($"{peer.Uri.ToHostname()}: View ({LastHash}) at height {Height} does not match with local view ({chainState.LastHash}) and remote weight ({Weight}) is higher compared to local ({chainState.Weight}). Performing sync...");
            syncService.Add(new Chain(peer, new(), Height));
        }
    }
}