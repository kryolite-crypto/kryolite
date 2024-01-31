using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;
using Microsoft.Extensions.DependencyInjection;
using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class NodeBroadcast : IPacket
{
    public string Url { get; set; }                 

    [MemoryPackConstructor]
    public NodeBroadcast(string url)
    {
        Url = url;
    }

    public NodeBroadcast(Uri uri)
    {
        Url = uri.ToString();
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var networkManager = scope.ServiceProvider.GetRequiredService<INetworkManager>();
        var meshNetwork = scope.ServiceProvider.GetRequiredService<IMeshNetwork>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NodeBroadcast>>();

        logger.LogInformation("Received NodeBroadcast from {hostname}", peer.Uri.ToHostname());

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
            // TODO: Rebroadcast
        }
    }
}
