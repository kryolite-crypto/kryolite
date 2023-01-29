using MessagePack;
using Microsoft.Extensions.Logging;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class QueryNodeList : IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"QueryNodeList received from {peer.Uri.ToHostname()}");

        var nodes = new List<NodeCandidate>();
        
        var randomized = context.NetworkManager.GetHosts()
            .OrderByDescending(x => x.LastSeen)
            .Take(100)
            .OrderBy(x => Guid.NewGuid())
            .Take(50);

        foreach (var host in randomized)
        {
            nodes.Add(new NodeCandidate(host.Url, host.ClientId));
        }

        var nodeList = new NodeList()
        {
            Nodes = nodes
        };


        var newHost = new NodeHost(peer.Uri)
        {
            ClientId = peer.Id,
            LastSeen = DateTime.UtcNow,
            IsReachable = peer.IsReachable
        };

        context.NetworkManager.AddHost(newHost);

        _ = peer.SendAsync(nodeList);
    }
}
