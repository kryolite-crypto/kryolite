using Kryolite.FastSerializer;

namespace Kryolite.Model;

public sealed class CallMethod : ITransactionPayload
{
    public string Method = string.Empty;
    public string[] Params = [];

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CALL_METHOD;
    }

    public int GetLength()
    {
        var length = Serializer.SizeOf(Method);

        foreach (var param in Params)
        {
            length += Serializer.SizeOf(param);
        }

        return length;
    }

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
