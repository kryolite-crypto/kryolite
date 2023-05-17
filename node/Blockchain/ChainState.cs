using System.Numerics;
using Kryolite.Shared;

namespace Kryolite.Node;

public class ChainState
{
    public int Id { get; set; } = 1;

    public long Height { get; set; } = -1;
    public long Blocks { get; set; }
    public int Epoch { get; set; }
    public SHA256Hash LastHash { get; set; } = new SHA256Hash();
    public Difficulty CurrentDifficulty { get; set; }
}
