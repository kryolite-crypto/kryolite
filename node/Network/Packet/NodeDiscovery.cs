using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeDiscovery : IPacket
{
    public Uri Uri { get; set; }

    public NodeDiscovery(Uri uri)
    {
        Uri = uri;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
        var meshNetwork = scope.ServiceProvider.GetRequiredService<IMeshNetwork>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeDiscovery>>();

        logger.LogInformation($"Received NodeDiscovery from {peer.Uri.ToHostname()}");

        var nodeHost = new NodeHost(Uri)
        {
            ClientId = peer.ClientId,
            LastSeen = DateTime.UtcNow,
            IsReachable = Connection.TestConnection(Uri)
        };

        networkManager.AddHost(nodeHost);

        if (meshNetwork.GetPeers().Count < Constant.MAX_PEERS)
        {
            _ = meshNetwork.ConnectToAsync(Uri);
        }

        if (nodeHost.IsReachable)
        {
            args.Rebroadcast = true;
        }
    }
}
