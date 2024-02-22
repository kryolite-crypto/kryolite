using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using MemoryPack;

namespace Kryolite.Node.Network;

[MemoryPackable]
public partial class PendingResponse
{
    public List<Block> Blocks { get; set; }
    public List<Vote> Votes { get; set; }
    public List<TransactionDto> Transactions { get; set; }

    public PendingResponse(List<Block> blocks, List<Vote> votes, List<TransactionDto> transactions)
    {
        Blocks = blocks;
        Votes = votes;
        Transactions = transactions;
    }
}
