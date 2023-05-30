using System.Numerics;
using Kryolite.Shared;
using Redbus.Events;

namespace Kryolite.Node;

public class ChainState : EventBase
{
    public int Id { get; set; } = 1;

    public BigInteger Weight { get; set; } = new BigInteger(0);
    public long Height { get; set; } = -1;
    public long Blocks { get; set; }
    public SHA256Hash LastHash { get; set; } = new SHA256Hash();
    public Difficulty CurrentDifficulty { get; set; }
}
