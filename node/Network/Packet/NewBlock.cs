using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class NewBlock : IPacket
{
    [Key(0)]
    public PosBlock Block { get; }

    public NewBlock(PosBlock block)
    {
        Block = block;
    }

    public async Task Handle(Peer peer, MessageEventArgs args, PacketContext context)
    {
        context.Logger.LogInformation($"Received block {Block.Height} from {args.Message.NodeId}");
        var chainState = context.BlockchainManager.GetChainState();

        if (chainState.POS.Height > Block.Height)
        {
            return;
        }

        if (chainState.POS.Height < (Block.Height - 1))
        {
            context.Logger.LogInformation($"Chain is behind received block (local = {chainState.POS.Height}, received = {Block.Height}), requesting chain sync...");
            var msg2 = new Message
            {
                Payload = new RequestChainSync
                {
                    StartBlock = chainState.POS.Height,
                    StartHash = chainState.POS.LastHash
                }
            };

            await peer.SendAsync(msg2);
            return;
        }

        if (context.BlockchainManager.AddBlock(Block, false, true)) 
        {
            args.Rebroadcast = true;
        }
    }
}