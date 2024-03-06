using System.Numerics;
using Kryolite.EventBus;

namespace Kryolite.Shared;

public sealed class ChainState : EventBase, ISerializable
{
    public long Id;
    public long TotalBlocks;
    public SHA256Hash ViewHash;
    public Difficulty CurrentDifficulty;
    public long TotalVotes;
    public long TotalTransactions;
    public ulong BlockReward;
    public BigInteger Weight;
    public BigInteger TotalWork;

    public ChainState()
    {
        ViewHash = new();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CHAINSTATE;
    }

    public int GetLength() =>
        Serializer.SizeOf(Id) +
        Serializer.SizeOf(TotalBlocks) +
        Serializer.SizeOf(ViewHash) +
        Serializer.SizeOf(CurrentDifficulty.Value) +
        Serializer.SizeOf(TotalVotes) +
        Serializer.SizeOf(TotalTransactions) +
        Serializer.SizeOf(BlockReward) +
        Serializer.SizeOf(Weight) +
        Serializer.SizeOf(TotalWork);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Id);
        serializer.Write(TotalBlocks);
        serializer.Write(ViewHash);
        serializer.Write(CurrentDifficulty.Value);
        serializer.Write(TotalVotes);
        serializer.Write(TotalTransactions);
        serializer.Write(BlockReward);
        serializer.Write(Weight);
        serializer.Write(TotalWork);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Id);
        serializer.Read(ref TotalBlocks);
        serializer.Read(ref ViewHash);
        serializer.Read(ref CurrentDifficulty.Value);
        serializer.Read(ref TotalVotes);
        serializer.Read(ref TotalTransactions);
        serializer.Read(ref BlockReward);
        serializer.Read(ref Weight);
        serializer.Read(ref TotalWork);
    }
}
