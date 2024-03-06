using Kryolite.Shared;

namespace Kryolite.Grpc.NodeService;

public class SyncResponse : ISerializable
{
    public bool ShouldSync;

    public SyncResponse()
    {

    }

    public SyncResponse(bool shouldSync)
    {
        ShouldSync = shouldSync;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.SYNC_RESPONSE;
    }

    public int GetLength() =>
        Serializer.SizeOf(ShouldSync);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(ShouldSync);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref ShouldSync);
    }
}
