using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Grpc.NodeService;

public class ViewListRequest : ISerializable
{
    public long StartHeight;
    public int BatchSize;

    public ViewListRequest()
    {

    }

    public ViewListRequest(long startHeight, int batchSize)
    {
        StartHeight = startHeight;
        BatchSize = batchSize;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VIEWLIST_REQUEST;
    }

    public int GetLength() =>
        Serializer.SizeOf(StartHeight) +
        Serializer.SizeOf(BatchSize);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(StartHeight);
        serializer.Write(BatchSize);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref StartHeight);
        serializer.Read(ref BatchSize);
    }
}