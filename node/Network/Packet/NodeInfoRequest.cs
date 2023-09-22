using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeInfoRequest : IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        if (peer.LastNodeInfo is not null && (DateTime.Now - peer.LastNodeInfo).Value.TotalSeconds < 10)
        {
            return;
        }

        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoRequest>>();

        logger.LogInformation($"NodeInfoRequest from {peer.Uri.ToHostname()}");

        var chainState = blockchainManager.GetChainState();
        var response = new NodeInfoResponse
        {
            Height = chainState.Height,
            Weight = chainState.Weight,
            LastHash = blockchainManager.GetLastView()?.TransactionId ?? SHA256Hash.NULL_HASH,
            CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        peer.LastNodeInfo = DateTime.Now;

        _ = peer.SendAsync(response);
    }
}