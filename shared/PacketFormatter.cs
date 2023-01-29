using System.Buffers;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MessagePack;
using MessagePack.Formatters;

namespace Kryolite.Shared;

public enum Packet : byte
{
    NodeInfo,
    Blockchain,
    NewBlock,
    QueryNodeInfo,
    RequestChainSync,
    TransactionData,
    VoteBatch,
    NodeDiscovery,
    NodeList,
    QueryNodeList,
    CallMethod,
    NewContract
}

public class PacketFormatter : IMessagePackFormatter<object>
{
    private delegate void SerializeMethod(object dynamicContractlessFormatter, ref MessagePackWriter writer, object value, MessagePackSerializerOptions options);
    private delegate object DeserializeMethod(object dynamicContractlessFormatter, ref MessagePackReader reader, MessagePackSerializerOptions options);

    private static readonly ConcurrentDictionary<Type, SerializeMethod> Serializers = new();
    private static readonly ConcurrentDictionary<Type, DeserializeMethod> Deserializers = new();

    private static readonly ConcurrentDictionary<Type, byte> TypeNameCache = new();
    private static readonly ConcurrentDictionary<byte, Type> TypeCache = new();

    public static void Register<T>(Packet packetId)
    {
        var type = typeof(T);

        var ti = type.GetTypeInfo();

        var formatterType = typeof(IMessagePackFormatter<>).MakeGenericType(type);
        var param0 = Expression.Parameter(typeof(object), "formatter");
        var param1 = Expression.Parameter(typeof(MessagePackWriter).MakeByRefType(), "writer");
        var param2 = Expression.Parameter(typeof(object), "value");
        var param3 = Expression.Parameter(typeof(MessagePackSerializerOptions), "options");
        var param4 = Expression.Parameter(typeof(MessagePackReader).MakeByRefType(), "reader");

        var serializeMethodInfo = formatterType.GetRuntimeMethod("Serialize", new[] { typeof(MessagePackWriter).MakeByRefType(), type, typeof(MessagePackSerializerOptions) });

        MethodCallExpression body = Expression.Call(
            Expression.Convert(param0, formatterType),
            serializeMethodInfo,
            param1,
            ti.IsValueType ? Expression.Unbox(param2, type) : Expression.Convert(param2, type),
            param3);

        var serializeMethod = Expression.Lambda<SerializeMethod>(body, param0, param1, param2, param3).Compile();

        var deserializeMethodInfo = formatterType.GetRuntimeMethod("Deserialize", new[] { typeof(MessagePackReader).MakeByRefType(), typeof(MessagePackSerializerOptions) });

        var deserialize = Expression.Call(
            Expression.Convert(param0, formatterType),
            deserializeMethodInfo,
            param4,
            param3);

        Expression deBody = deserialize;

        if (ti.IsValueType)
        {
            deBody = Expression.Convert(deserialize, typeof(object));
        }

        var deserializeMethod = Expression.Lambda<DeserializeMethod>(deBody, param0, param4, param3).Compile();

        TypeNameCache.TryAdd(type, (byte)packetId);
        TypeCache.TryAdd((byte)packetId, type);
        Serializers.TryAdd(type, serializeMethod);
        Deserializers.TryAdd(type, deserializeMethod);
    }

    public void Serialize(ref MessagePackWriter writer, object value, MessagePackSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNil();
            return;
        }

        Type type = value.GetType();

        if (!TypeNameCache.TryGetValue(type, out var packetId))
        {
            throw new Exception("serializer not registered, type = " + type.ToString());
        }

        var formatter = options.Resolver.GetFormatterDynamic(type);

        if (!Serializers.TryGetValue(type, out var serializeMethod))
        {
            throw new Exception("serializer not registered, type = " + type.ToString());
        }

        IBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();

        var scratchWriter = new MessagePackWriter(buffer);
        scratchWriter.Write(packetId);
        serializeMethod(formatter, ref scratchWriter, value, options);
        scratchWriter.Flush();

        // mark as extension with code 100
        writer.WriteExtensionFormat(new ExtensionResult((sbyte)100, ((ArrayBufferWriter<byte>)buffer).WrittenMemory.ToArray()));
    }

    public object Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        if (reader.NextMessagePackType == MessagePackType.Extension)
        {
            MessagePackReader peekReader = reader.CreatePeekReader();
            ExtensionHeader ext = peekReader.ReadExtensionFormatHeader();
            if (ext.TypeCode == 100)
            {
                reader = peekReader; // commit the experimental read made earlier.
                var result = this.DeserializeByTypeName(reader.ReadByte(), ref reader, options);
                return result;
            }
        }

        Console.WriteLine("fallback");

        // fallback
        return DynamicObjectTypeFallbackFormatter.Instance.Deserialize(ref reader, options);
    }

    private object DeserializeByTypeName(byte packetId, ref MessagePackReader byteSequence, MessagePackSerializerOptions options)
    {
        if (!TypeCache.TryGetValue(packetId, out var type))
        {
            throw new Exception("type not registered, packetId: " + packetId);
        }

        options.ThrowIfDeserializingTypeIsDisallowed(type);

        var formatter = options.Resolver.GetFormatterDynamic(type);

        if (!Deserializers.TryGetValue(type, out var deserializeMethod))
        {
            throw new Exception("deserializer not registered, packetId = " + packetId);
        }

        return deserializeMethod(formatter, ref byteSequence, options);
    }
}
