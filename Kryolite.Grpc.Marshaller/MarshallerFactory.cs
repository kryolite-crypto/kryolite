﻿using ServiceModel.Grpc.Channel;
using Grpc.Core;
using Kryolite.Shared;
using ServiceModel.Grpc.Configuration;
using System.Runtime.CompilerServices;
using Kryolite.Grpc.NodeService;
using System.Buffers;

namespace ServiceModel.Grpc.Marshaller;

public class MarshallerFactory : IMarshallerFactory
{
    public static MarshallerFactory Instance = new MarshallerFactory();
    public Marshaller<T> CreateMarshaller<T>() => new(Serialize, Deserialize<T>);

    internal static void Serialize<T>(T value, SerializationContext context)
    {
        Console.WriteLine("Serialize: " + typeof(T));
        switch (value)
        {
            case Message:
                Serializer.Serialize(new SerializableMessage(), context.GetBufferWriter());
                break;

            case Message<long> msg:
                Serializer.Serialize(msg.Value1, context.GetBufferWriter());
                break;

            case Message<byte[][]> msg:
                Serializer.Serialize(new JaggedArrayMessage(msg.Value1), context.GetBufferWriter());
                break;

            case Message<SHA256Hash> msg:
                Serializer.Serialize(new SerializableMessage<SHA256Hash>(msg.Value1), context.GetBufferWriter());
                break;

            case Message<PublicKey> msg:
                Serializer.Serialize(new SerializableMessage<PublicKey>(msg.Value1), context.GetBufferWriter());
                break;

            case Message<SyncRequest> msg:
                Serializer.Serialize(new SerializableMessage<SyncRequest>(msg.Value1), context.GetBufferWriter());
                break;

            case Message<SyncResponse> msg:
                Serializer.Serialize(new SerializableMessage<SyncResponse>(msg.Value1), context.GetBufferWriter());
                break;

            case Message<AuthResponse> msg:
                Serializer.Serialize(new SerializableMessage<AuthResponse>(msg.Value1), context.GetBufferWriter());
                break;

            case Message<AuthRequest> msg:
                Serializer.Serialize(new SerializableMessage<AuthRequest>(msg.Value1), context.GetBufferWriter());
                break;

            default:
                throw new InvalidCastException("No serialization handler registered for type: " + typeof(T).ToString());
        }

        context.Complete();
    }

    internal static T Deserialize<T>(DeserializationContext context)
    {
        Console.WriteLine("Deserialize: " + typeof(T));

        switch (typeof(T))
        {
            case var t when t == typeof(Message):
                return ToMessage<T>();

            case var t when t == typeof(Message<long>):
                return ToMessage<T>(BitConverter.ToInt64(context.PayloadAsNewBuffer()));

            case var t when t == typeof(Message<byte[][]>):
                return ToMessage<T>(context.PayloadAsReadOnlySequence());

            case var t when t == typeof(Message<SHA256Hash>):
                return ToMessage<T, SHA256Hash>(context.PayloadAsReadOnlySequence());

            case var t1 when t1 == typeof(Message<PublicKey>):
                return ToMessage<T, PublicKey>(context.PayloadAsReadOnlySequence());

            case var t2 when t2 == typeof(Message<SyncRequest>):
                return ToMessage<T, SyncRequest>(context.PayloadAsReadOnlySequence());

            case var t3 when t3 == typeof(Message<SyncResponse>):
                return ToMessage<T, SyncResponse>(context.PayloadAsReadOnlySequence());

            case var t when t == typeof(Message<AuthRequest>):
                return ToMessage<T, AuthRequest>(context.PayloadAsReadOnlySequence());

            case var t when t == typeof(Message<AuthResponse>):
                return ToMessage<T, AuthResponse>(context.PayloadAsReadOnlySequence());

            default:
                throw new InvalidCastException("No deserialization handler registered for type: " + typeof(T).ToString());
        }
    }

    private static T ToMessage<T>()
    {
        return (T)(object)new Message();
    }

    private static T ToMessage<T, T1>(ReadOnlySequence<byte> buffer) where T1 : ISerializable, new()
    {
        var msg = Serializer.Deserialize<SerializableMessage<T1>>(buffer);
        return (T)(object)new Message<T1>(msg.Value!);
    }

    private static T ToMessage<T>(long value)
    {
        return (T)(object)new Message<long>(value);
    }

    private static T ToMessage<T>(ReadOnlySequence<byte> buffer)
    {
        var msg = Serializer.Deserialize<JaggedArrayMessage>(buffer);
        Console.WriteLine(msg.Value.Length);
        return (T)(object)new Message<byte[][]>(msg.Value);
    }
}
