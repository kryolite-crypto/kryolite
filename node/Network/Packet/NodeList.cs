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

        var serverId = context.MeshNetwork.GetServerId();

        // Filter out self from nodes
        Nodes = Nodes.Where(x => x.ClientId != serverId)
            .ToList();

        Parallel.ForEach(Nodes, node => {
            var host = new NodeHost(node.Url)
            {
                ClientId = node.ClientId,
                LastSeen = DateTime.UtcNow,
                IsReachable = Connection.TestConnection(node.Url)
            };

            context.NetworkManager.AddHost(host);
        });

        var peers = context.MeshNetwork.GetPeers()
            .Where(x => x.Value.ConnectionType == ConnectionType.OUT)
            .ToDictionary(x => x.Key, x => x.Value);

        _ = Task.Run(async () => {
            var randomized = context.NetworkManager.GetHosts()
                .Where(x => !peers.ContainsKey(x.ClientId))
                .OrderByDescending(x => x.LastSeen)
                .Take(100)
                .OrderBy(x => Guid.NewGuid())
                .Take(50);

            var connected = peers.Count;
            var allowedConnections = Constant.MAX_PEERS * 1.5;

            foreach (var node in randomized)
            {
                if (connected >= allowedConnections)
                {
                    break;
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
