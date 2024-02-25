using MemoryPack;
using ServiceModel.Grpc.Channel;

namespace ServiceModel.Grpc.MemoryPackaArshaller;

[MemoryPackable]
internal readonly partial struct SerializableMessage
{

}

[MemoryPackable]
internal readonly partial struct SerializableMessage<T>
{
    internal readonly Message<T> Message;

    [MemoryPackConstructor]
    public SerializableMessage(T value)
    {
        Message = new Message<T>(value);
    }

    public SerializableMessage(Message<T> message)
    {
        Message = message;
    }

    [MemoryPackInclude]
    public T Value => Message.Value1;
}

[MemoryPackable]
internal readonly partial struct SerializableMessage<T1, T2>
{
    internal readonly Message<T1, T2> Message;

    [MemoryPackConstructor]
    public SerializableMessage(T1 value1, T2 value2)
    {
        Message = new Message<T1, T2>(value1, value2);
    }

    public SerializableMessage(Message<T1, T2> message)
    {
        Message = message;
    }

    [MemoryPackInclude]
    public T1 Value1 => Message.Value1;
    [MemoryPackInclude]
    public T2 Value2 => Message.Value2;
}

[MemoryPackable]
internal readonly partial struct SerializableMessage<T1, T2, T3>
{
    internal readonly Message<T1, T2, T3> Message;

    [MemoryPackConstructor]
    public SerializableMessage(T1 value1, T2 value2, T3 value3)
    {
        Message = new Message<T1, T2, T3>(value1, value2, value3);
    }

    public SerializableMessage(Message<T1, T2, T3> message)
    {
        Message = message;
    }

    [MemoryPackInclude]
    public T1 Value1 => Message.Value1;
    [MemoryPackInclude]
    public T2 Value2 => Message.Value2;
    [MemoryPackInclude]
    public T3 Value3 => Message.Value3;
}
