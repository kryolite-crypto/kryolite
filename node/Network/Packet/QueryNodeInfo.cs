using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class QueryNodeInfo : IPacket
{
    public async Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Node query received from {args.Message.NodeId}");

        var chainState3 = context.BlockchainManager.GetChainState();
        var response = new Message
        {
            Payload = new NodeInfo
            {
                Height = chainState3.POS.Height,
                TotalWork = chainState3.POW.TotalWork,
                LastHash = context.BlockchainManager.GetLastBlockhash() ?? new SHA256Hash(),
                CurrentTime = DateTime.UtcNow,
                ConnectedPeers = context.MeshNetwork.GetPeers().Count
            }
        };

        await peer.SendAsync(response);

        context.NetworkManager.AddHost(new NodeHost(peer.Url)
        {
            ClientId = args.Message.NodeId,
            LastSeen = DateTime.Now
        });
    }
}
