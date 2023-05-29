using MessagePack;
using Microsoft.Extensions.Logging;
using Kryolite.Shared;
using static Kryolite.Node.NetworkManager;

namespace Kryolite.Node;

[MessagePackObject]
public class QueryNodeInfo : IPacket
{
    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Node query received from {peer.Uri.ToHostname()}");

        var chainState = context.BlockchainManager.GetChainState();
        var response = new NodeInfo
        {
            Height = chainState.Height,
            Weight = chainState.Weight,
            LastHash = context.BlockchainManager.GetLastView().TransactionId ?? new SHA256Hash(),
            CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        _ = peer.SendAsync(response);
    }
}
