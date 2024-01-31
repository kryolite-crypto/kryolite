using System.Numerics;
using Kryolite.EventBus;
using Kryolite.Shared;
using MemoryPack;

namespace Kryolite.Node;

[MemoryPackable]
public partial class ChainState : EventBase
{
    public long Id {  get; set; }
    public BigInteger Weight { get; set; } = new BigInteger(0);
    /// <summary>
    /// Total blocks in chain
    /// </summary>
    public long Blocks { get; set; }
    public SHA256Hash ViewHash { get; set; } = new SHA256Hash();
    public Difficulty CurrentDifficulty { get; set; }
    public long Votes { get; set; }
    public long Transactions { get; set; }
    public ulong BlockReward { get; set; }
}
