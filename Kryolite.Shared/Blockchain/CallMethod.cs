
using System.Text.Json.Serialization;
using Kryolite.ByteSerializer;

namespace Kryolite.Shared;

public sealed class CallMethod : ITransactionPayload
{
    [JsonPropertyName("method")]
    public string Method = string.Empty;

    [JsonPropertyName("params")]
    public string[]? Params;

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CALL_METHOD;
    }

    public int GetLength() =>
        Serializer.SizeOf(Method) +
        Serializer.SizeOf(Params);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Method);
        serializer.Write(Params);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Method);
        serializer.Read(ref Params);
    }
}
