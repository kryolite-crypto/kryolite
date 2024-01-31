using System.Security.Cryptography;
using MemoryPack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MemoryPackable]
public partial class View
{
    public long Id { get; set; }
    public long Timestamp { get; init; }
    public SHA256Hash LastHash { get; init; } = SHA256Hash.NULL_HASH;
    public PublicKey PublicKey { get; init; } = PublicKey.NULL_PUBLIC_KEY;
    public Signature Signature { get; set; } = Signature.NULL_SIGNATURE;
    public List<SHA256Hash> Transactions { get; set; } = new();
    public List<SHA256Hash> Rewards { get; set; } = new();
    public List<SHA256Hash> Votes { get; set; } = new();
    public List<SHA256Hash> Blocks { get; set; } = new();
    public List<SHA256Hash> ScheduledTransactions { get; set; } = new();

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(LastHash!);
        stream.Write(PublicKey!);

        foreach (var hash in Transactions.Order())
        {
            stream.Write(hash);
        }

        foreach (var vote in Votes.Order())
        {
            stream.Write(vote);
        }

        foreach (var block in Blocks.Order())
        {
            stream.Write(block);
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(LastHash!);
        stream.Write(PublicKey!);

        foreach (var hash in Transactions.Order())
        {
            stream.Write(hash);
        }

        foreach (var vote in Votes.Order())
        {
            stream.Write(vote);
        }

        foreach (var block in Blocks.Order())
        {
            stream.Write(block);
        }

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = new Ed25519();
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(LastHash!);
        stream.Write(PublicKey!);

        foreach (var hash in Transactions.Order())
        {
            stream.Write(hash);
        }

        foreach (var vote in Votes.Order())
        {
            stream.Write(vote);
        }

        foreach (var block in Blocks.Order())
        {
            stream.Write(block);
        }

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature);
    }

    public bool ShouldClearVotes()
    {
        return Id % Constant.VOTE_INTERVAL == 1;
    }

    public bool IsEpoch()
    {
        return Id % Constant.EPOCH_LENGTH == 0;
    }

    public bool IsMilestone()
    {
        return Id % Constant.VOTE_INTERVAL == 0;
    }
}
