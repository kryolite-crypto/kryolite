using MessagePack;
using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

public class Block : Transaction
{
    public Difficulty Difficulty { get; set; }
    public SHA256Hash ParentHash { get; set; } = new SHA256Hash();

    public static Block Create(Address wallet, long timestamp, SHA256Hash parentHash, Difficulty difficulty)
    {
        var block = new Block()
        {
            TransactionType = TransactionType.BLOCK,
            To = wallet,
            Value = Constant.BLOCK_REWARD,
            Timestamp = timestamp,
            ParentHash = parentHash,
            Difficulty = difficulty
        };

        block.Data = MessagePackSerializer.Serialize(new BlockPayload(block));

        return block;
    }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(To!);
        stream.Write(new MerkleTree(Validates).RootHash);
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
            Buffer = basehash.Buffer.Concat(Pow.Buffer).ToArray()
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
