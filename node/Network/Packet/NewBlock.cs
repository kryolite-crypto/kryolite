using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

[MessagePackObject]
public class NewBlock : IPacket
{
    [Key(0)]
    public Block Block { get; }

    public NewBlock(Block block)
    {
        Block = block;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, PacketContext context)
    {
        /*context.Logger.LogInformation($"Received block {Block.Height} from {peer.Uri.ToHostname()}");
        var chainState = context.BlockchainManager.GetChainState();

        if (chainState.POS.Height > Block.Height)
        {
            context.Logger.LogInformation($"Chain is ahead received block (local = {chainState.POS.Height}, received = {Block.Height}), sending current chain info...");
            var info = new NodeInfo
            {
                CurrentTime = DateTime.UtcNow,
                Height = chainState.POS.Height,
                TotalWork = chainState.POW.TotalWork,
                LastHash = chainState.POW.LastHash
            };

            _ = peer.SendAsync(info);
            return;
        }

        if (chainState.POS.Height < (Block.Height - 1) && !ChainObserver.InProgress)
        {
            context.Logger.LogInformation($"Chain is behind received block (local = {chainState.POS.Height}, received = {Block.Height}), requesting chain sync...");
            var sync = new RequestChainSync
            {
                StartBlock = chainState.POS.Height,
                StartHash = chainState.POS.LastHash
            };

            _ = peer.SendAsync(sync);
            return;
        }

        if (context.BlockchainManager.AddBlock(Block, false, true)) 
        {
            args.Rebroadcast = true;
        }*/
    }
}