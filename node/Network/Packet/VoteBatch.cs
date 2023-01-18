using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class VoteBatch : IPacket
{
    [Key(0)]
    public IList<Vote> Votes { get; set; } = new List<Vote>();

    public Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogDebug($"Received {Votes.Count} votes from {args.Message.NodeId}");

        var validVotes = context.BlockchainManager.AddVotes(Votes);

        foreach (var vote in validVotes)
        {
            context.VoteBuffer.Post(vote);
        }

        return Task.CompletedTask;
    }
}
