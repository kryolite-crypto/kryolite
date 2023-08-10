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
    public SHA256Hash? LastHash { get; init; }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfo>>();

        logger.LogInformation($"Received NodeInfo from {peer.Uri.ToHostname()}");
        var chainState = blockchainManager.GetChainState();

        if (Weight > chainState.Weight)
        {
            var lastView = blockchainManager.GetLastView();

            var msg = new RequestChainSync
            {
                LastHash = lastView.TransactionId
            };

            logger.LogInformation($"{peer.Uri.ToHostname()} has greater weight ({Weight}) compared to local ({chainState.Weight}). Initiating chain download...");

            _ = peer.SendAsync(msg);
        }
    }
}
