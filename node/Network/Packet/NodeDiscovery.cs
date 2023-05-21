using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeDiscovery : IPacket
{
    public Uri Uri { get; set; }

    public NodeDiscovery(Uri uri)
    {
        Uri = uri;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received NodeDiscovery from {peer.Uri.ToHostname()}");

        var nodeHost = new NodeHost(Uri)
        {
            ClientId = peer.ClientId,
            LastSeen = DateTime.UtcNow,
            IsReachable = Connection.TestConnection(Uri)
        };

        context.NetworkManager.AddHost(nodeHost);

        if (context.MeshNetwork.GetOutgoingConnections().Count < Constant.MAX_PEERS)
        {
            _ = context.MeshNetwork.ConnectToAsync(Uri);
        }

        args.Rebroadcast = true;
    }
}
