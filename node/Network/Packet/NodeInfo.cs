using System.Numerics;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeInfo : IPacket
{
    [Key(0)]
    public DateTime CurrentTime { get; init; } // TODO unixtime
    [Key(1)]
    public long Height { get; init; }
    [Key(2)]
    public BigInteger TotalWork { get; init; }
    [Key(3)]
    public SHA256Hash LastHash { get; init; }
    [Key(4)]
    public int ConnectedPeers { get; init; }

    public async Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received NodeInfo from {args.Message.NodeId}");
        var chainState2 = context.BlockchainManager.GetChainState();

        var nodeHost = new NodeHost(peer.Url)
        {
            NodeInfo = this,
            LastSeen = DateTime.Now
        };

        context.NetworkManager.AddHost(nodeHost);

        var msg = new Message
        {
            Payload = new RequestChainSync
            {
                StartBlock = chainState2.POS.Height,
                StartHash = chainState2.POS.LastHash
            }
        };

        var totalWork = context.BlockchainManager.GetTotalWork();

        if (TotalWork > totalWork)
        {
            context.Logger.LogInformation($"{args.Message.NodeId} has greater TotalWork ({TotalWork}) compared to local ({totalWork}). Requesting chain sync");
            await peer.SendAsync(msg);
        }
    }
}
