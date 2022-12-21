using System.Numerics;
using Kryolite.Shared;

namespace Kryolite.Node;

public class ChainState
{
    public int Id { get; set; } = 1;
    public PohjolaChain POW { get; set; } = new();
    public TuonelaChain POS { get; set; } = new();
}

public class PohjolaChain
{
    public int Id { get; set; } = 1;
    public long Height { get; set; }
    public BigInteger TotalWork { get; set; } = new BigInteger(0);
    public Difficulty CurrentDifficulty {get; set; } = new Difficulty { Value = 0 };
    public int CurrentReward { get; set; } = 1000 * 1000000;
    public SHA256Hash LastHash { get; set; }
}

public class TuonelaChain
{
    public int Id { get; set; } = 1;
    public bool Active { get => SampoCount > 0; }
    public long Height { get; set; }
    public int SampoCount { get; set; }
    public SHA256Hash LastHash { get; set; }
}
