using Kryolite.Shared;
using Kryolite.Shared.Dto;

namespace Kryolite.Grpc.NodeService;

public class NodeListResponse : ISerializable
{
    public List<NodeDto> Nodes;

    public NodeListResponse()
    {
        Nodes = [];
    }

    public NodeListResponse(List<NodeDto> nodes)
    {
        Nodes = nodes;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.NODELIST_RESPONSE;
    }

    public int GetLength() =>
        Serializer.SizeOf(Nodes);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Nodes);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Nodes);
    }
}