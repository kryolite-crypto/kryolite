using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class NonceConverter : JsonConverter<Nonce>
{
    public override Nonce Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        return Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, Nonce value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
