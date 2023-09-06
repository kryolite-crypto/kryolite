using System.Data.Common;
using System.Numerics;
using Kryolite.Shared;
using MessagePack;
using Redbus.Events;

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
}
