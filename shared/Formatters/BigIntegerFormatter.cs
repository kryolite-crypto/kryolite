using MessagePack;
using MessagePack.Formatters;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Formatters;

public class BigIntegerFormatter : IMessagePackFormatter<object>
{
    public object Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.IsNil)
        {
            return null!;
        }

        var bytes = reader.ReadBytes();

        if (bytes is null)
        {
            return null!;
        }

        return new BigInteger(bytes.Value.ToArray());
    }

    public void Serialize(ref MessagePackWriter writer, object value, MessagePackSerializerOptions options)
    {
        if (value is not BigInteger bigint)
        {
            writer.WriteNil();
            return;
        }

        writer.Write(bigint.ToByteArray());
    }
}

public class BigIntegerResolver : IFormatterResolver
{
    public static BigIntegerResolver Instance = new();

    BigIntegerFormatter formatter = new();

    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        if (typeof(T) == typeof(BigInteger))
        {
            return (IMessagePackFormatter<T>)formatter;
        }

        return null;
    }
}