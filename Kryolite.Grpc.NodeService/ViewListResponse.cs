using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Grpc.NodeService;

public class ViewListResponse : ISerializable
{
    public List<ViewResponse> Views;

    public ViewListResponse()
    {
        Views = [];
    }

    public ViewListResponse(List<ViewResponse> views)
    {
        Views = views;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VIEWLIST_RESPONSE;
    }

    public int GetLength() =>
        Serializer.SizeOf(Views);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Views);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Views);
    }
}