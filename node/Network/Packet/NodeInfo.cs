using System.Net.Sockets;
using System.Numerics;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeInfo : IPacket
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
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfo>>();

        logger.LogInformation($"Received NodeInfo from {peer.Uri.ToHostname()}");

        if (peer.IsSyncInProgress)
        {
            // Do not request chain sync if previous sync is ongoing
            return;
        }

        var chainState = blockchainManager.GetChainState();

        if (LastHash != chainState.LastHash && Weight > chainState.Weight)
        {
            logger.LogInformation($"{peer.Uri.ToHostname()}: View ({LastHash}) at height {Height} does not match with local view ({chainState.LastHash}) and remote weight ({Weight}) is higher compared to local ({chainState.Weight}). Performing sync...");

            var msg = new RequestChainSync
            {
                LastHash = chainState?.LastHash ?? SHA256Hash.NULL_HASH
            };

            _ = peer.SendAsync(msg);
        }
    }
}
