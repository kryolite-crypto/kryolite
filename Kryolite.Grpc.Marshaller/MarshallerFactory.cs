using ServiceModel.Grpc.Channel;
using Grpc.Core;
using Kryolite.Shared;
using ServiceModel.Grpc.Configuration;
using System.Runtime.CompilerServices;

namespace ServiceModel.Grpc.Marshaller;

public class MarshallerFactory : IMarshallerFactory
{
    public static MarshallerFactory Instance = new MarshallerFactory();
    public Marshaller<T> CreateMarshaller<T>() => new(Serialize, Deserialize<T>);

    internal static void Serialize<T>(T value, SerializationContext context)
    {
        Console.WriteLine("Serialize " + typeof(T));

        switch (typeof(T).Name)
        {
            case "Message":
                Console.WriteLine("msg0");
                Serializer.Serialize(new SerializableMessage(), context.GetBufferWriter());
                break;
            case "Message`1":
                Console.WriteLine("msg1");
                var msg1 = Unsafe.As<Message<ISerializable>>(value);

                if (msg1 is null)
                {
                    goto default;
                }

                Serializer.Serialize(msg1.Value1, context.GetBufferWriter());
                break;
            case "Message`2":
                Console.WriteLine("msg2");
                var msg2 = Unsafe.As<Message<ISerializable, ISerializable>>(value);

                if (msg2 is null)
                {
                    goto default;
                }

                Serializer.Serialize(msg2.Value1, context.GetBufferWriter());
                Serializer.Serialize(msg2.Value2, context.GetBufferWriter());
                break;
            case "Message`3":
                Console.WriteLine("msg3");
                var msg3 = Unsafe.As<Message<ISerializable, ISerializable, ISerializable>>(value);

                if (msg3 is null)
                {
                    goto default;
                }

                Serializer.Serialize(msg3.Value1, context.GetBufferWriter());
                Serializer.Serialize(msg3.Value2, context.GetBufferWriter());
                Serializer.Serialize(msg3.Value3, context.GetBufferWriter());
                break;
            default:
                throw new InvalidCastException(typeof(T).ToString());
        }

        Console.WriteLine("done");
        context.Complete();
    }

    internal static T Deserialize<T>(DeserializationContext context)
    {
        try{
        Console.WriteLine("Deserialize " + typeof(T));

        switch (typeof(T).Name)
        {
            case "Message":
                return (T)(object)new Message();
            case "Message`1":
                Console.WriteLine("Message1");
                var msg1 = new Message<ISerializable>
                {
                    Value1 = Serializer.Deserialize<ISerializable>(context.PayloadAsReadOnlySequence())
                };

                ref var b = ref Unsafe.AsRef(ref msg1);
                return Unsafe.As<Message<ISerializable>, T>(ref b);
            case "Message`2":
                var msg2 = new Message<ISerializable, ISerializable>
                {
                    Value1 = Serializer.Deserialize<ISerializable>(context.PayloadAsReadOnlySequence()),
                    Value2 = Serializer.Deserialize<ISerializable>(context.PayloadAsReadOnlySequence())
                };

                return (T)(object)msg2;
            case "Message`3":
                var msg3 = new Message<ISerializable, ISerializable, ISerializable>
                {
                    Value1 = Serializer.Deserialize<ISerializable>(context.PayloadAsReadOnlySequence()),
                    Value2 = Serializer.Deserialize<ISerializable>(context.PayloadAsReadOnlySequence()),
                    Value3 = Serializer.Deserialize<ISerializable>(context.PayloadAsReadOnlySequence())
                };

                return (T)(object)msg3;
            default:
                throw new InvalidCastException("Target type: " + typeof(T).ToString());
        }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            throw new InvalidCastException("Target type: " + typeof(T).ToString());
        }
    }
}
