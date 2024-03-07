using System.Numerics;
using Kryolite.Shared;

namespace Kryolite.Grpc.NodeService;

public class SyncRequest : ISerializable
{
    public PublicKey PublicKey;
    public SHA256Hash ViewHash;
    public BigInteger Weight;

    public SyncRequest()
    {
        PublicKey = new();
        ViewHash = new();
    }

    public SyncRequest(PublicKey publicKey, SHA256Hash viewHash, BigInteger weight)
    {
        PublicKey = publicKey;
        ViewHash = viewHash;
        Weight = weight;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.SYNC_REQUEST;
    }

    public int GetLength() =>
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(ViewHash) +
        Serializer.SizeOf(Weight);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(ViewHash);
        serializer.Write(Weight);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PublicKey);
        serializer.Read(ref ViewHash);
        serializer.Read(ref Weight);
    }
}
