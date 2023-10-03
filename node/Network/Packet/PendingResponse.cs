using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class PendingResponse : IPacket
{
    [Key(0)]
    public List<Block> Blocks { get; set; }
    [Key(1)]
    public List<Vote> Votes { get; set; }
    [Key(2)]
    public List<TransactionDto> Transactions { get; set; }

    public PendingResponse(List<Block> blocks, List<Vote> votes, List<TransactionDto> transactions)
    {
        Blocks = blocks;
        Votes = votes;
        Transactions = transactions;
    }

    public void Handle(Peer peer, MessageReceivedEventArgs args, IServiceProvider provider)
    {
        throw new NotImplementedException();
    }
}
