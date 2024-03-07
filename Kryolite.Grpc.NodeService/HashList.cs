using Kryolite.Shared;

namespace Kryolite.Grpc.NodeService;

public class HashList : ISerializable
{
    public List<SHA256Hash> Hashes;

    public HashList()
    {
        Hashes = [];
    }

    public HashList(List<SHA256Hash> hashes)
    {
        Hashes = hashes;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.HASH_LIST;
    }

    public int GetLength() =>
        Serializer.SizeOf(Hashes);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Hashes);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Hashes);
    }
}