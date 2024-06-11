using System.Numerics;
using System.Text.Json.Serialization;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Shared.Dto;

public partial class ChainStateDto
{
    public long Id { get; set; }
    public BigInteger Weight { get; set; }
    public BigInteger TotalWork { get; set; }
    public long Blocks { get; set; }
    public SHA256Hash LastHash { get; set; }
    public string CurrentDifficulty { get; set; }
    public long Votes { get; set; }
    public long Transactions { get; set; }
    public ulong TotalActiveStake { get; set; }
    public long LastFinalizedHeight { get; set; }

    [JsonConstructor]
    public ChainStateDto(long id, BigInteger weight, BigInteger totalWork, long blocks, SHA256Hash lastHash, string currentDifficulty, long votes, long transactions, ulong totalActiveStake, long lastFinalizedHeight)
    {
        Id = id;
        Weight = weight;
        TotalWork = totalWork;
        Blocks = blocks;
        LastHash = lastHash;
        CurrentDifficulty = currentDifficulty;
        Votes = votes;
        Transactions = transactions;
        TotalActiveStake = totalActiveStake;
        LastFinalizedHeight = lastFinalizedHeight;
    }

    public ChainStateDto(ChainState chainState)
    {
        Id = chainState.Id;
        Weight = chainState.Weight;
        TotalWork = chainState.TotalWork;
        Blocks = chainState.TotalBlocks;
        LastHash = chainState.ViewHash;
        CurrentDifficulty = chainState.CurrentDifficulty.ToString();
        Votes = chainState.TotalVotes;
        Transactions = chainState.TotalTransactions;
        TotalActiveStake = chainState.TotalActiveStake;
        LastFinalizedHeight = chainState.LastFinalizedHeight;
    }
}
