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

public struct SerializableMessage<T> : ISerializable where T : ISerializable, new()
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
        return (byte)SerializerEnum.JAGGED_ARRAY;
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

public struct JaggedArrayMessage : ISerializable
{
    public byte[][] Value;

    public JaggedArrayMessage()
    {
        Value = [];
    }

    public JaggedArrayMessage(byte[][] value)
    {
        Value = value;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.JAGGED_ARRAY;
    }

    public int GetLength()
    {
        var length = sizeof(int);

        for (var i = 0; i < Value.Length; i++)
        {
            length += Serializer.SizeOf(Value[i]);
        }

        return length;
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Value.Length);

        for (var i = 0; i < Value.Length; i++)
        {
            serializer.Write(Value[i]);
        }
    }

    public void Deserialize(ref Serializer serializer)
    {
        var length = 0;
        serializer.Read(ref length);

        Value = new byte[length][];

        for (var i = 0; i < length; i++)
        {
            Value[i] = [];
            serializer.Read(ref Value[i]);
        }
    }
}
