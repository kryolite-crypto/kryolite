using System.Numerics;
using System.Runtime.Serialization;
using Kryolite.EventBus;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class ChainState : EventBase
{
    public long Id {  get; set; }
    /// <summary>
    /// Total blocks in chain
    /// </summary>
    public long Blocks { get; set; }
    public SHA256Hash ViewHash { get; set; } = SHA256Hash.NULL_HASH;
    public Difficulty CurrentDifficulty { get; set; }
    /// <summary>
    /// Total votes in chain
    /// </summary>
    public long Votes { get; set; }
    /// <summary>
    /// TotalTransaction in chain
    /// </summary>
    public long Transactions { get; set; }
    public ulong BlockReward { get; set; }
    [MemoryPackInclude]
    private byte[] _weight { get; set; } = [];
    [MemoryPackInclude]
    private byte[] _totalWork { get; set; } = [];

    [MemoryPackIgnore]
    public BigInteger Weight { get; set; }
    [MemoryPackIgnore]
    public BigInteger TotalWork { get; set; }

    [MemoryPackOnSerializing]
    void OnSerializing()
    {
        _weight = Weight.ToByteArray();
        _totalWork = TotalWork.ToByteArray();
    }

    [MemoryPackOnDeserialized]
    void OnDeserialized()
    {
        Weight = new BigInteger(_weight);
        TotalWork = new BigInteger(_totalWork);
    }
}
