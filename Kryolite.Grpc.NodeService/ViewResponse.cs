using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Grpc.NodeService;

public class ViewResponse : ISerializable
{
    public View? View;
    public List<Block> Blocks = [];
    public List<Vote> Votes = [];
    public List<TransactionDto> Transactions = [];

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VIEW_RESPONSE;
    }

    public int GetLength() =>
        Serializer.SizeOfN(View) +
        Serializer.SizeOf(Blocks) +
        Serializer.SizeOf(Votes) +
        Serializer.SizeOf(Transactions);

    public ViewResponse Create<ViewResponse>() where ViewResponse : new()
    {
        return new ViewResponse();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.WriteN(View);
        serializer.Write(Blocks);
        serializer.Write(Votes);
        serializer.Write(Transactions);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.ReadN(ref View);
        serializer.Read(ref Blocks);
        serializer.Read(ref Votes);
        serializer.Read(ref Transactions);
    }
}

public class ViewRangeResponse : ISerializable
{
    public List<ViewResponse> Views = [];

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VIEW_RANGE_RESPONSE;
    }

    public int GetLength()
        => Serializer.SizeOf(Views);

    public ViewRangeResponse Create<ViewRangeResponse>() where ViewRangeResponse : new()
    {
        return new ViewRangeResponse();
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Write(Views);
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Read(ref Views);
    }
}
