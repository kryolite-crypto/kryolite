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

        Parallel.ForEach(Nodes, node => {
            var host = new NodeHost(node.Url)
            {
                ClientId = node.ClientId,
                LastSeen = DateTime.UtcNow,
                IsReachable = Connection.TestConnection(node.Url)
            };

            context.NetworkManager.AddHost(host);
        });

        _ = Task.Run(async () => {
            var randomized = context.NetworkManager.GetHosts()
                .OrderByDescending(x => x.LastSeen)
                .Take(100)
                .OrderBy(x => Guid.NewGuid())
                .Take(50);

            var connected = context.MeshNetwork.GetOutgoingConnections().Count;
            var allowedConnections = Constant.MAX_PEERS * 1.5;

            foreach (var node in randomized)
            {
                if (connected >= allowedConnections)
                {
                    return;
                }

                if (await context.MeshNetwork.ConnectToAsync(node.Url))
                {
                    connected++;

                    var nextPeer = context.MeshNetwork.GetPeer(node.Url);

                    if (nextPeer != null)
                    {
                        await nextPeer.SendAsync(new QueryNodeInfo());
                    }
                }
            }

            var chainState = context.BlockchainManager.GetChainState();

            var sync = new RequestChainSync
            {
                StartBlock = chainState.POS.Height,
                StartHash = chainState.POS.LastHash
            };

            await peer.SendAsync(sync);
        });
    }
}
