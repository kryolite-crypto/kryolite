using System.Text.Json.Serialization;
using Kryolite.ByteSerializer;
using Kryolite.EventBus;
using Kryolite.Type;

namespace Kryolite.Shared;

public sealed class Validator : EventBase, ISerializable
{
    public Address NodeAddress;
    public Address RewardAddress;
    public ulong Stake;
    public bool Active;
    public long LastActiveHeight;

    [JsonIgnore]
    public bool Changed = false;

    public Validator()
    {
        NodeAddress = new();
        RewardAddress = new();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VALIDATOR;
    }

    public int GetLength() =>
        Serializer.SizeOf(NodeAddress) +
        Serializer.SizeOf(RewardAddress) +
        Serializer.SizeOf(Stake) +
        Serializer.SizeOf(Active) +
        Serializer.SizeOf(LastActiveHeight);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(NodeAddress);
        serializer.Write(RewardAddress);
        serializer.Write(Stake);
        serializer.Write(Active);
        serializer.Write(LastActiveHeight);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref NodeAddress);
        serializer.Read(ref RewardAddress);
        serializer.Read(ref Stake);
        serializer.Read(ref Active);
        serializer.Read(ref LastActiveHeight);
    }
}
