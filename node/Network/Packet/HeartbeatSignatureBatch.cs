using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class HeartbeatSignatureBatch : IPacket
{
    [Key(0)]
    public IList<HeartbeatSignature> HeartbeatSignatures { get; set; } = new List<HeartbeatSignature>();

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        /*context.Logger.LogDebug($"Received {Votes.Count} votes from {peer.Uri.ToHostname()}");

        var validVotes = context.BlockchainManager.AddVotes(Votes);

        foreach (var vote in validVotes)
        {
            context.VoteBuffer.Post(vote);
        }*/
    }
}
