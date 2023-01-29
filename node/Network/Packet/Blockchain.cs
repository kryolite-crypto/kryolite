using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class Blockchain : IPacket
{
    [Key(0)]
    public List<PosBlock>? Blocks { get; set; }

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received blockchain from {peer.Uri.ToHostname()}");

        if (Blocks != null) 
        {
            var chain = new Chain(peer, Blocks);
            context.SyncBuffer.Post<Chain>(chain);
        }
    }
}
