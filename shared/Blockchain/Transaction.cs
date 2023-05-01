using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

public class Transaction : IComparable<Transaction>
{
    [JsonIgnore]
    [IgnoreMember]
    public Guid Id { get; set; }

    [IgnoreMember]
    public SHA256Hash TransactionId
    {
        get => CalculateHash();
        private set { }
    }

    [Key(0)]
    public TransactionType TransactionType { get; set; }
    [Key(1)]
    public virtual PublicKey? PublicKey { get; set; }
    [Key(2)]
    public virtual Address? To { get; set; } = new Address();
    [Key(3)]
    public ulong Value { get; set; }
    [Key(4)]
    public SHA256Hash Pow { get; set; } = new SHA256Hash();
    [Key(5)]
    public byte[]? Data { get; set; }
    [Key(6)]
    public long Timestamp { get; set; }
    [Key(7)]
    public virtual Signature? Signature { get; set; }
    [Key(8)]
    public List<Transaction> Validates { get; set; } = new();
    [Key(9)]
    public List<Transaction> ValidatedBy { get; set; } = new();

    [IgnoreMember]
    public List<Effect> Effects { get; set; } = new();

    [IgnoreMember]
    public Address From
    {
        get => PublicKey?.ToAddress() ?? new Address();
        private set { }
    }

    public virtual void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes((short)TransactionType));
        stream.Write(PublicKey ?? throw new Exception("public key required when signing transactions"));
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));

        foreach (var tx in Validates.OrderBy(x => x.TransactionId))
        {
            stream.Write(tx.CalculateHash());
        }

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public virtual bool Verify()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes((short)TransactionType));
        stream.Write(PublicKey ?? throw new Exception("public key required when verifying signed transaction (malformed transaction?)"));
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));

        foreach (var tx in Validates.OrderBy(x => x.TransactionId))
        {
            stream.Write(tx.CalculateHash());
        }

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature ?? throw new Exception("trying to verify null signature"));
    }

    public virtual SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        if (TransactionType == TransactionType.PAYMENT || TransactionType == TransactionType.CONTRACT)
        {
            stream.Write(PublicKey ?? throw new Exception("public key required when hashing payment"));
            stream.Write(Signature ?? throw new Exception("signature required when hashing payment"));
        }

        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));

        foreach (var tx in Validates)
        {
            stream.Write(tx.CalculateHash());
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)CalculateHash(), (ReadOnlySpan<byte>)other!.CalculateHash());
    }
}
