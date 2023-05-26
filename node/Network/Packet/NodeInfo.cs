using System.Net.Sockets;
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

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received NodeInfo from {peer.Uri.ToHostname()}");
        var chainState2 = context.BlockchainManager.GetChainState();

        var totalWork = context.BlockchainManager.GetTotalWork();

        if (TotalWork > totalWork)
        {
            var msg = new RequestChainSync
            {
                StartBlock = chainState2.POS.Height,
                StartHash = chainState2.POS.LastHash
            };

            context.Logger.LogInformation($"{peer.Uri.ToHostname()} has greater TotalWork ({TotalWork}) compared to local ({totalWork}). Requesting chain sync");

            _ = peer.SendAsync(msg);
        }
    }
}
