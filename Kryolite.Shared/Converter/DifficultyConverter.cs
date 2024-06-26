using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class DifficultyConverter : JsonConverter<Difficulty>
{
    public override Difficulty Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options)
    {
        return new Difficulty
        {
            Value = reader.GetUInt32()
        };
    }

    public override void Write(Utf8JsonWriter writer, Difficulty value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }
}
