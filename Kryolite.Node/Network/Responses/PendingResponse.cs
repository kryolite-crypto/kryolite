using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Node.Network;

public class PendingResponse : ISerializable
{
    public List<Block> Blocks;
    public List<Vote> Votes;
    public List<TransactionDto> Transactions;

    public PendingResponse()
    {
        Blocks = new();
        Votes = new();
        Transactions = new();
    }

    public PendingResponse(List<Block> blocks, List<Vote> votes, List<TransactionDto> transactions)
    {
        Blocks = blocks;
        Votes = votes;
        Transactions = transactions;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.PENDING_RESPONSE;
    }

    public int GetLength() =>
        Serializer.SizeOf(Blocks) +
        Serializer.SizeOf(Votes) +
        Serializer.SizeOf(Transactions);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Blocks);
        serializer.Write(Votes);
        serializer.Write(Transactions);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Blocks);
        serializer.Read(ref Votes);
        serializer.Read(ref Transactions);
    }
}
