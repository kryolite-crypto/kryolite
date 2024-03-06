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

public readonly struct SerializableMessage2 : ISerializable
{
    internal readonly Message<ISerializable> Message;

    public SerializableMessage2(ISerializable value)
    {
        Message = new Message<ISerializable>(value);
    }

    public SerializableMessage2(Message<ISerializable> message)
    {
        Message = message;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE_1;
    }

    public int GetLength() =>
        Serializer.SizeOf(Message.Value1);

    public void Deserialize(ref Serializer serializer)
    {
        serializer.WriteN(Message.Value1);
    }

    public void Serialize(ref Serializer serializer)
    {
        //ISerializable? value1 = default;
        //serializer.ReadN(ref value1);

    }

    //public T1 Value => Message.Value1;
}

public readonly struct SerializableMessage<T1, T2> : ISerializable 
    where T1 : ISerializable, new()
    where T2 : ISerializable, new()
{
    internal readonly Message<T1, T2> Message;

    public SerializableMessage(T1 value, T2 value2)
    {
        Message = new Message<T1, T2>(value, value2);
    }

    public SerializableMessage(Message<T1, T2> message)
    {
        Message = message;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE_2;
    }

    public int GetLength() =>
        Serializer.SizeOf(Message.Value1) +
        Serializer.SizeOf(Message.Value2);

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Write(Message.Value1);
        serializer.Write(Message.Value2);
    }

    public void Serialize(ref Serializer serializer)
    {
        T1 value1 = new();
        T2 value2 = new();
    
        serializer.Read(ref value1);
        serializer.Read(ref value2);

        Message.Value1 = value1;
        Message.Value2 = value2;
    }

    public T1 Value1 => Message.Value1;
    public T2 Value2 => Message.Value2;
}

public readonly struct SerializableMessage<T1, T2, T3> : ISerializable 
    where T1 : ISerializable, new()
    where T2 : ISerializable, new()
    where T3 : ISerializable, new()
{
    internal readonly Message<T1, T2, T3> Message;

    public SerializableMessage(T1 value, T2 value2, T3 value3)
    {
        Message = new Message<T1, T2, T3>(value, value2, value3);
    }

    public SerializableMessage(Message<T1, T2, T3> message)
    {
        Message = message;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.MESSAGE_3;
    }

    public int GetLength() =>
        Serializer.SizeOf(Message.Value1) +
        Serializer.SizeOf(Message.Value2) +
        Serializer.SizeOf(Message.Value3);

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Write(Message.Value1);
        serializer.Write(Message.Value2);
        serializer.Write(Message.Value3);
    }

    public void Serialize(ref Serializer serializer)
    {
        T1 value1 = new();
        T2 value2 = new();
        T3 value3 = new();
    
        serializer.Read(ref value1);
        serializer.Read(ref value2);
        serializer.Read(ref value3);

        Message.Value1 = value1;
        Message.Value2 = value2;
        Message.Value3 = value3;
    }

    public T1 Value1 => Message.Value1;
    public T2 Value2 => Message.Value2;
    public T3 Value3 => Message.Value3;
}