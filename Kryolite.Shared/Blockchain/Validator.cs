using Kryolite.ByteSerializer;
using Kryolite.EventBus;

namespace Kryolite.Shared;

public sealed class Validator : EventBase, ISerializable
{
    public Address NodeAddress;
    public Address RewardAddress;
    public ulong Stake;

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
        Serializer.SizeOf(Stake);

    public Validator Create<Validator>() where Validator : new()
    {
        return new Validator();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(NodeAddress);
        serializer.Write(RewardAddress);
        serializer.Write(Stake);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref NodeAddress);
        serializer.Read(ref RewardAddress);
        serializer.Read(ref Stake);
    }
}
