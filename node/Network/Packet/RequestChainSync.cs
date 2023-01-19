using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class RequestChainSync : IPacket
{
    [Key(0)]
    public long StartBlock { get; init; }
    [Key(1)]
    public byte[]? StartHash { get; init; }

    public async Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Chain sync requested from {args.Message.NodeId}");

        var block = context.BlockchainManager.GetPosBlock(StartBlock);

        var chain = new Blockchain();

        if (block == null) {
            chain.Blocks = context.BlockchainManager.GetPosFrom(0);
            goto answer;
        }

        if (!Enumerable.SequenceEqual(block.GetHash().Buffer, StartHash!)) {
            chain.Blocks = context.BlockchainManager.GetPosFrom(0);
            goto answer;
        }

        if (StartBlock == context.BlockchainManager.GetCurrentHeight()) {
            return;
        }

        chain.Blocks = context.BlockchainManager.GetPosFrom(StartBlock);

answer:
        var answer = new Message()
        {
            Payload = chain
        };

        await peer.SendAsync(answer);
    }
}