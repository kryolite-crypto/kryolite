using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node;

[MessagePackObject]
public class QueryNodeInfo : IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<QueryNodeInfo>>();

        logger.LogInformation($"Node query received from {peer.Uri.ToHostname()}");

        var chainState = blockchainManager.GetChainState();
        var response = new NodeInfo
        {
            Height = chainState.Height,
            Weight = chainState.Weight,
            LastHash = blockchainManager.GetLastView().TransactionId ?? new SHA256Hash(),
            CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        _ = peer.SendAsync(response);
    }
}
