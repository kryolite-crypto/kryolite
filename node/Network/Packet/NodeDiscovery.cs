using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;
using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeDiscovery : IPacket
{
    [Key(0)]
    public string Url { get; set; }                 

    public NodeDiscovery(Uri uri)
    {
        Url = uri.ToString();
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
        var meshNetwork = scope.ServiceProvider.GetRequiredService<IMeshNetwork>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeDiscovery>>();

        logger.LogInformation($"Received NodeDiscovery from {peer.Uri.ToHostname()}");

        var uri = new Uri(Url);

        var nodeHost = new NodeHost(uri)
        {
            ClientId = peer.ClientId,
            LastSeen = DateTime.UtcNow,
            IsReachable = Connection.TestConnection(uri)
        };

        networkManager.AddHost(nodeHost);

        if (meshNetwork.GetPeers().Count < Constant.MAX_PEERS)
        {
            _ = meshNetwork.ConnectToAsync(uri);
        }

        if (nodeHost.IsReachable)
        {
            args.Rebroadcast = true;
        }
    }
}
