using Kryolite.ByteSerializer;

namespace Kryolite.Shared.Dto;

public class NodeDto : ISerializable
{
    public PublicKey PublicKey;
    public string Url;
    public DateTimeOffset FirstSeen;
    public DateTimeOffset LastSeen;
    public string Version;

    public NodeDto()
    {
        PublicKey = new();
        Url = string.Empty;
        Version = string.Empty;
    }

    public NodeDto(PublicKey publicKey, string url, DateTime firstSeen, DateTime lastSeen, string version)
    {
        PublicKey = publicKey;
        Url = url;
        FirstSeen = firstSeen;
        LastSeen = lastSeen;
        Version = version;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.NODE_DTO;
    }

    public int GetLength()
    {
        return
            Serializer.SizeOf(PublicKey) +
            Serializer.SizeOf(Url) +
            Serializer.SizeOf(FirstSeen) +
            Serializer.SizeOf(LastSeen);
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(Url);
        serializer.Write(FirstSeen);
        serializer.Write(LastSeen);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PublicKey);
        serializer.Read(ref Url);
        serializer.Read(ref FirstSeen);
        serializer.Read(ref LastSeen);
    }
}
