using Kryolite.ByteSerializer;

namespace Kryolite.Transport.Websocket;

public class Message : ISerializable
{
    public Message()
    {
        
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE;
    }

    public int GetLength() => 0;

    public void Serialize(ref Serializer serializer)
    {

    }

    public void Deserialize(ref Serializer serializer)
    {

    }
}

public class Message<T1> : ISerializable
    where T1 : ISerializable, new()
{
    public T1 Value1;

    public Message()
    {
        Value1 = default!;
    }

    public Message(T1 value1)
    {
        Value1 = value1;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE_1;
    }

    public int GetLength() =>
        Serializer.SizeOf(Value1);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Value1);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Value1);
    }
}
