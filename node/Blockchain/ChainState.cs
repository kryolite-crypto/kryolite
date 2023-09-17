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
    public long Height { get; set; } = -1;
    [Key(3)]
    public long Blocks { get; set; }
    [Key(4)]
    public SHA256Hash LastHash { get; set; } = new SHA256Hash();
    [Key(5)]
    public Difficulty CurrentDifficulty { get; set; }
    [Key(6)]
    public int Votes { get; set; }
    [Key(7)]
    public int Transactions { get; set; }
}
