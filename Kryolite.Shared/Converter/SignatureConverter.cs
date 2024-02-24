using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class SignatureConverter : JsonConverter<Signature>
{
    public override Signature Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        return Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, Signature value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
