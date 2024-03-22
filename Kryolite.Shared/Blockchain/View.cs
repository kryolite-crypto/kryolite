using System.Security.Cryptography;
using Kryolite.ByteSerializer;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

public sealed class View : ISerializable
{
    public long Id;
    public long Timestamp;
    public SHA256Hash LastHash;
    public PublicKey PublicKey;
    public Signature Signature;
    public List<SHA256Hash> Transactions;
    public List<SHA256Hash> Votes;
    public List<SHA256Hash> Blocks;
    public List<SHA256Hash> Rewards;
    public List<SHA256Hash> ScheduledTransactions;

    public View()
    {
        LastHash = new();
        PublicKey = new();
        Signature = new();
        Transactions = new();
        Votes = new();
        Blocks = new();
        Rewards = new();
        ScheduledTransactions = new();
    }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write((ReadOnlySpan<byte>)LastHash);
        stream.Write((ReadOnlySpan<byte>)PublicKey);

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

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VIEW;
    }

    public int GetLength()
    {
        return
            Serializer.SizeOf(Id) +
            Serializer.SizeOf(Timestamp) +
            Serializer.SizeOf(LastHash) +
            Serializer.SizeOf(PublicKey) +
            Serializer.SizeOf(Signature) +
            Serializer.SizeOf(Transactions) +
            Serializer.SizeOf(Votes) +
            Serializer.SizeOf(Blocks) +
            Serializer.SizeOf(Rewards) +
            Serializer.SizeOf(ScheduledTransactions);
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Id);
        serializer.Write(Timestamp);
        serializer.Write(LastHash);
        serializer.Write(PublicKey);
        serializer.Write(Signature);
        serializer.Write(Transactions);
        serializer.Write(Votes);
        serializer.Write(Blocks);
        serializer.Write(Rewards);
        serializer.Write(ScheduledTransactions);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Id);
        serializer.Read(ref Timestamp);
        serializer.Read(ref LastHash);
        serializer.Read(ref PublicKey);
        serializer.Read(ref Signature);
        serializer.Read(ref Transactions);
        serializer.Read(ref Votes);
        serializer.Read(ref Blocks);
        serializer.Read(ref Rewards);
        serializer.Read(ref ScheduledTransactions);
    }
}
