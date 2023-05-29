using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class TransactionBatch : IPacket
{
    [Key(0)]
    public List<TransactionDto>? Transactions { get; set; }

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received blockchain from {peer.Uri.ToHostname()}");

        /*if (Blocks != null && !ChainObserver.InProgress)
        {
            var chain = new Chain(peer, Blocks);
            context.SyncBuffer.Post<Chain>(chain);
        }*/
    }
}
