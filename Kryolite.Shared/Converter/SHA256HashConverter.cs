using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kryolite.Type;

namespace Kryolite.Shared;

public class SHA256HashConverter : JsonConverter<SHA256Hash>
{
    public override SHA256Hash Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options)
    {
        return Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, SHA256Hash value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
