using System.Numerics;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Shared.Dto;

public partial class ChainStateDto(ChainState chainState)
{
    public long Id { get; set; } = chainState.Id;
    public BigInteger Weight { get; set; } = chainState.Weight;
    public BigInteger TotalWork { get; set; } = chainState.TotalWork;
    public long Blocks { get; set; } = chainState.Blocks;
    public SHA256Hash LastHash { get; set; } = chainState.ViewHash;
    public string CurrentDifficulty { get; set; } = chainState.CurrentDifficulty.ToString();
    public long Votes { get; set; } = chainState.Votes;
    public long Transactions { get; set; } = chainState.Transactions;
}
