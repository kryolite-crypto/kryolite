using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Transaction : IComparable<Transaction>
{
    [Key(0)]
    public ulong Id { get; set; }
    [Key(1)]
    public SHA256Hash TransactionId { get; set; }
    [Key(2)]
    public long? Height { get; set; }
    [Key(3)]
    public TransactionType TransactionType { get; set; }
    [Key(4)]
    public PublicKey? PublicKey {
        get => pk;
        set {
            pk = value;
            From = pk?.ToAddress() ?? new Address();
        }
    }

    [Key(5)]
    public Address? To { get; set; }
    [Key(6)]
    public long Value { get; set; }
    [Key(7)]
    public byte[]? Pow { get; set; }
    [Key(8)]
    public byte[]? Data { get; set; }
    [Key(9)]
    public long Timestamp { get; set; }
    [Key(10)]
    public Signature? Signature { get; set; }
    [Key(11)]
    public ExecutionResult ExecutionResult { get; set; }
    [Key(12)]
    public List<SHA256Hash> Parents { get; set; } = new List<SHA256Hash>();
    [Key(13)]
    public List<Effect> Effects { get; set; } = new();

    [IgnoreMember]
    public bool IsVerified { get; set; }

    [IgnoreMember]
    public Address? From { get; private set; }

    private PublicKey? pk;

    public Transaction()
    {
        TransactionId = SHA256Hash.NULL_HASH;
    }

    public Transaction(TransactionDto tx, List<SHA256Hash> parents)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("payment requires public key");
        To = tx.To;
        Value = tx.Value;
        Pow = tx.Pow;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("payment requires signature");
        Parents = parents;
        TransactionId = CalculateHash();
    }

    public virtual void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey ?? throw new Exception("public key required when signing transactions"));

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        if (To is not null)
        {
            stream.Write(To);
        }
        
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(Pow);

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public virtual bool Verify()
    {
        var algorithm = new Ed25519();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey ?? throw new Exception("public key required when verifying signed transaction (malformed transaction?)"));

        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(Pow);

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature ?? throw new Exception("trying to verify null signature"));
    }

    public virtual SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (TransactionType == TransactionType.PAYMENT || TransactionType == TransactionType.CONTRACT)
        {
            stream.Write(PublicKey ?? throw new Exception("public key required when hashing payment"));
        }

        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(Pow);

        if (Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        return sha256.ComputeHash(stream.ToArray());
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)TransactionId.Buffer, (ReadOnlySpan<byte>)(other?.TransactionId.Buffer ?? new byte[0]));
    }
}
