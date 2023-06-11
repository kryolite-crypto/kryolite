using Kryolite.Shared.Dto;
using MessagePack;
using System.Reactive;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Block : Transaction
{
    [IgnoreMember]
    public Difficulty Difficulty { get; set; }
    [IgnoreMember]
    public SHA256Hash ParentHash { get; set; } = new SHA256Hash();

    public Block()
    {

    }

    public Block(Address wallet, long timestamp, SHA256Hash parentHash, Difficulty difficulty, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.BLOCK;
        To = wallet;
        Value = Constant.BLOCK_REWARD;
        Timestamp = timestamp;
        ParentHash = parentHash;
        Difficulty = difficulty;
        Parents = parents;
        Data = MessagePackSerializer.Serialize(new BlockPayload(this));
        TransactionId = CalculateHash();
    }

    public Block(TransactionDto tx, List<SHA256Hash> parents)
    {
        TransactionType = TransactionType.BLOCK;
        To = tx.To;
        Value = Constant.BLOCK_REWARD;
        Timestamp = tx.Timestamp;
        Data = tx.Data;
        Pow = tx.Pow;
        Parents = parents;
        TransactionId = CalculateHash();
    }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(To!);

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Write(BitConverter.GetBytes(Difficulty.Value));
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(Data);

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public bool VerifyNonce()
    {
        var basehash = GetHash();
        var concat = new Concat
        {
            Buffer = basehash.Buffer.Concat(Pow ?? new byte[0]).ToArray()
        };

        var hash = Grasshopper.Hash(ParentHash, concat);

        var target = Difficulty.ToTarget();
        var result = hash.ToBigInteger();

        return result.CompareTo(target) <= 0;
    }
}

[MessagePackObject]
public class BlockPayload
{
    [Key(0)]
    public Difficulty Difficulty { get; set; }
    [Key(1)]
    public SHA256Hash ParentHash { get; set; } = new SHA256Hash();

    public BlockPayload()
    {

    }

    public BlockPayload(Block block)
    {
        Difficulty = block.Difficulty;
        ParentHash = block.ParentHash;
    }
}
