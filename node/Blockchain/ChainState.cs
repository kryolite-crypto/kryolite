using System.Numerics;
using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class ChainState
{
    [BsonId(false)]
    public int Id { get; set; } = 0;
    public long Height { get; set; }
    public BigInteger TotalWork { get; set; } = new BigInteger(0);
    public Difficulty CurrentDifficulty {get; set; } = new Difficulty { Value = 0 };
    public int CurrentReward { get; set; } = 1000 * 1000000;
}
