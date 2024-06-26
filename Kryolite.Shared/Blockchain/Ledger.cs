using System.Text.Json.Serialization;
using Kryolite.ByteSerializer;
using Kryolite.EventBus;
using Kryolite.Type;

namespace Kryolite.Shared;

public sealed class Ledger : EventBase, ISerializable
{
    public Address Address;
    public ulong Balance;
    public bool Locked;
    public ulong Pending;
    
    [JsonIgnore]
    public bool Changed = false;

    public Ledger()
    {
        Address = new();
    }

    public Ledger(Address address)
    {
        Address = address;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.LEDGER;
    }

    public int GetLength() =>
        Serializer.SizeOf(Address) +
        Serializer.SizeOf(Balance) +
        Serializer.SizeOf(Locked) +
        Serializer.SizeOf(Pending);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Address);
        serializer.Write(Balance);
        serializer.Write(Locked);
        serializer.Write(Pending);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Address);
        serializer.Read(ref Balance);
        serializer.Read(ref Locked);
        serializer.Read(ref Pending);
    }
}
