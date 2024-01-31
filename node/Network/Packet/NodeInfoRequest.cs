using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class NodeInfoRequest : IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var blockchainManager = scope.ServiceProvider.GetRequiredService<IStoreManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeInfoRequest>>();

        logger.LogInformation("NodeInfoRequest from {hostname}", peer.Uri.ToHostname());

        var chainState = blockchainManager.GetChainState();
        var response = new NodeInfoResponse
        {
            Height = chainState.Id,
            Weight = chainState.Weight,
            LastHash = chainState.ViewHash,
            CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        _ = peer.SendAsync(response);
    }
}
