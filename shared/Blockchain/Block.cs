using System.Security.Cryptography;

namespace Kryolite.Shared.Blockchain;

public class Block : Transaction
{
    public long Height;
    public Difficulty Difficulty { get; set; }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(new MerkleTree(Validates).RootHash);
        stream.Write(BitConverter.GetBytes(Difficulty.Value));
        stream.Write(BitConverter.GetBytes(Timestamp));

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
