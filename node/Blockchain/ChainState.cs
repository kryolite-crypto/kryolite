using System.Data.Common;
using System.Numerics;
using DuckDB.NET.Data;
using Kryolite.Shared;
using Redbus.Events;

namespace Kryolite.Node;

public class ChainState : EventBase
{
    public BigInteger Weight { get; set; } = new BigInteger(0);
    public long Height { get; set; } = -1;
    public long Blocks { get; set; }
    public SHA256Hash LastHash { get; set; } = new SHA256Hash();
    public Difficulty CurrentDifficulty { get; set; }

    public static ChainState Read(DbDataReader reader)
    {
        using var ms = new MemoryStream();

        reader.GetStream(0).CopyTo(ms);

        return new ChainState
        {
            Weight = new BigInteger(ms.ToArray()),
            Height = reader.GetInt64(1),
            Blocks = reader.GetInt64(2),
            LastHash = reader.GetString(3),
            CurrentDifficulty = new Difficulty { Value = (uint)reader.GetInt32(4)}
        };
    }
}
