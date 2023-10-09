using System.Numerics;
using Kryolite.EventBus;
using Kryolite.Shared;
using MessagePack;

namespace Kryolite.Node;

[MessagePackObject]
public class ChainState : EventBase
{
    [Key(0)]
    public long Id {  get; set; }
    [Key(1)]
    public BigInteger Weight { get; set; } = new BigInteger(0);
    [Key(2)]
    public long Blocks { get; set; }
    [Key(3)]
    public SHA256Hash ViewHash { get; set; } = new SHA256Hash();
    [Key(4)]
    public Difficulty CurrentDifficulty { get; set; }
    [Key(5)]
    public long Votes { get; set; }
    [Key(6)]
    public long Transactions { get; set; }
}
