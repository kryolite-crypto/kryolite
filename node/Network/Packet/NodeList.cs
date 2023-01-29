using System.Threading.Tasks.Dataflow;
using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeList : IPacket
{
    [Key(0)]
    public List<NodeCandidate> Nodes { get; set; } = new();

    public async void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received NodeList from {peer.Uri.ToHostname()}");

        foreach (var node in Nodes)
        {
            var host = new NodeHost(node.Url)
            {
                ClientId = node.ClientId,
                LastSeen = DateTime.UtcNow,
                IsReachable = Connection.TestConnection(node.Url)
            };

            context.NetworkManager.AddHost(host);
        }

        _ = Task.Run(async () => {
            var randomized = context.NetworkManager.GetHosts()
                .OrderByDescending(x => x.LastSeen)
                .Take(100)
                .OrderBy(x => Guid.NewGuid())
                .Take(50);

            var connected = 0; //context.MeshNetwork.GetOutgoingConnections().Count;

            foreach (var node in randomized)
            {
                if (connected >= Constant.MAX_PEERS)
                {
                    break;
                }

                if (await context.MeshNetwork.ConnectToAsync(node.Url))
                {
                    connected++;

                    var peer = context.MeshNetwork.GetPeer(node.Url);
                    await peer.SendAsync(new QueryNodeInfo());
                }
            }
        });
    }
}
