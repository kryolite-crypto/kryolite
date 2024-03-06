using System.Text;

namespace Kryolite.Shared.Dto;

public class NodeDto : ISerializable
{
    public PublicKey PublicKey;
    public string Url;
    public DateTimeOffset LastSeen;

    public NodeDto()
    {
        PublicKey = new();
        Url = string.Empty;
    }

    public NodeDto(PublicKey publicKey, string url, DateTime lastSeen)
    {
        PublicKey = publicKey;
        Url = url;
        LastSeen = lastSeen;
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
            Serializer.SizeOf(LastSeen);
    }

    public NodeDto Create<NodeDto>() where NodeDto : new()
    {
        return new NodeDto();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(Url);
        serializer.Write(LastSeen);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PublicKey);
        serializer.Read(ref Url);
        serializer.Read(ref LastSeen);
    }
}
