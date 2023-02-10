using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class DifficultyConverter : JsonConverter<Difficulty>
{
    public override Difficulty Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var bytes = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();

        return new Difficulty
        {
            Value = BitConverter.ToUInt32(bytes)
        };
    }

    public override void Write(Utf8JsonWriter writer, Difficulty value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }
}
