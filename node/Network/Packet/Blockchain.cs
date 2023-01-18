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

    public Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received blockchain from {args.Message.NodeId}");

        if (Blocks != null) 
        {
            peer.Blockchain = Blocks;
            context.SyncBuffer.Post<Peer>(peer);
        }

        return Task.CompletedTask;
    }
}
