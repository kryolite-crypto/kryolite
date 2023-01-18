using System.Threading.Tasks.Dataflow;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class NodeList : IPacket
{
    [Key(0)]
    public List<NodeCandidate> Nodes { get; set; } = new();

    public Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received NodeList from ${args.Message.NodeId}");

        foreach (var node in Nodes)
        {
            context.DiscoveryBuffer.Post(node);
        }

        return Task.CompletedTask;
    }
}
