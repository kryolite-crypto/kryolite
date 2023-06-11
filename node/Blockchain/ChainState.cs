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
    //[Key(1)]
    [IgnoreMember]
    public BigInteger Weight { get; set; } = new BigInteger(0);
    [Key(1)]
    public long Height { get; set; } = -1;
    [Key(2)]
    public long Blocks { get; set; }
    [Key(3)]
    public SHA256Hash LastHash { get; set; } = new SHA256Hash();
    [Key(4)]
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
