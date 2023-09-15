using Kryolite.Shared.Dto;
using MessagePack;
using System.Collections.Immutable;
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
    [IgnoreMember]
    public SHA256Hash Nonce { get; set; } = new SHA256Hash();

    public Block()
    {

    }

    public Block(Address wallet, long timestamp, SHA256Hash parentHash, Difficulty difficulty, ImmutableList<SHA256Hash> parents, SHA256Hash nonce)
    {
        TransactionType = TransactionType.BLOCK;
        To = wallet;
        Value = Constant.BLOCK_REWARD;
        Timestamp = timestamp;
        Parents = parents;
        ParentHash = parentHash;
        Difficulty = difficulty;
        Nonce = nonce;
        Data = MessagePackSerializer.Serialize(new BlockPayload(this));
        TransactionId = CalculateHash();
    }

    public Block(TransactionDto tx, ImmutableList<SHA256Hash> parents)
    {
        TransactionType = TransactionType.BLOCK;
        To = tx.To;
        Value = Constant.BLOCK_REWARD;
        Timestamp = tx.Timestamp;
        Data = tx.Data;
        Parents = parents.Distinct().ToImmutableList();

        var blockPayload = MessagePackSerializer.Deserialize<BlockPayload>(tx.Data);
        Difficulty = blockPayload.Difficulty;
        ParentHash = blockPayload.ParentHash;
        Nonce = blockPayload.Nonce;

        TransactionId = CalculateHash();
    }

    public override void Sign(PrivateKey privateKey)
    {
        throw new NotSupportedException();
    }

    public override bool Verify()
    {
        var basehash = GetHash();
        var concat = new Concat
        {
            Buffer = basehash.Buffer.Concat(Nonce.Buffer ?? new byte[0]).ToArray()
        };

        var hash = Grasshopper.Hash(ParentHash, concat);

        var target = Difficulty.ToTarget();
        var result = hash.ToBigInteger();

        return result.CompareTo(target) <= 0;
    }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(To ?? throw new Exception("missing required field 'block.to'"));
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data.AsSpan().Slice(0, 40));
        stream.Write(BitConverter.GetBytes(Timestamp));

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }
}

[MessagePackObject]
public class BlockPayload
{
    [Key(0)]
    public Difficulty Difficulty { get; set; }
    [Key(1)]
    public SHA256Hash ParentHash { get; set; } = new SHA256Hash();
    [Key(2)]
    public SHA256Hash Nonce { get; set; } = new SHA256Hash();

    public BlockPayload()
    {

    }

    public BlockPayload(Block block)
    {
        Difficulty = block.Difficulty;
        ParentHash = block.ParentHash;
        Nonce = block.Nonce;
    }
}
