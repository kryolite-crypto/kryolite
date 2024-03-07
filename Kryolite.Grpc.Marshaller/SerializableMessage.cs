using Kryolite.Shared;
using ServiceModel.Grpc.Channel;

namespace ServiceModel.Grpc.Marshaller;

public readonly struct SerializableMessage : ISerializable
{
    internal readonly Message Message;

    public SerializableMessage()
    {
        Message = new Message();
    }

    public SerializableMessage(Message message)
    {
        Message = message;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE;
    }

    public int GetLength()
    {
        return 0;
    }

    public void Deserialize(ref Serializer serializer)
    {

    }

    public void Serialize(ref Serializer serializer)
    {

    }
}

public struct SerializableMessage<T> : ISerializable where T : ISerializable?, new()
{
    public T? Value;

    public SerializableMessage()
    {

    }

    public SerializableMessage(T value)
    {
        Value = value;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE_1;
    }

    public int GetLength() =>
        Serializer.SizeOfN(Value);

    public void Serialize(ref Serializer serializer)
    {
        serializer.WriteN(Value);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.ReadN(ref Value);
    }
}
