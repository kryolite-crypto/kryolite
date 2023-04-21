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

        var chainState3 = context.BlockchainManager.GetChainState();
        var response = new NodeInfo
        {
            Height = chainState3.POS.Height,
            TotalWork = chainState3.POW.TotalWork,
            LastHash = context.BlockchainManager.GetLastBlockhash() ?? new SHA256Hash(),
            CurrentTime = DateTime.UtcNow
        };

        _ = Task.Run(() => {
            context.NetworkManager.AddHost(new NodeHost(peer.Uri)
            {
                ClientId = peer.Id,
                LastSeen = DateTime.UtcNow,
                IsReachable = Connection.TestConnection(peer.Uri)
            });
        });

        _ = peer.SendAsync(response);
    }
}
