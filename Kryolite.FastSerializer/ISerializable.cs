using System.Buffers;

namespace Kryolite.FastSerializer;

public interface ISerializable
{
    byte GetSerializerId();
    int GetLength();
    void Serialize(ref Serializer serializer);
    void Deserialize(ref Serializer serializer);
}
