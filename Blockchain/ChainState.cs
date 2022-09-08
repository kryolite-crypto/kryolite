using System.Numerics;

namespace Marccacoin;

public class ChainState
{
    public int _id { get; set; } = 0;
    public long Height;
    public BigInteger TotalWork = new BigInteger(0);
    public Difficulty CurrentDifficulty = new Difficulty { Value = 0 };
}