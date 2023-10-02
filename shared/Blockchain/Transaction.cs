using System.Security.Cryptography;
using Kryolite.EventBus;
using Kryolite.Shared.Dto;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Transaction : EventBase, IComparable<Transaction>
{
    [Key(0)]
    public TransactionType TransactionType { get; init; }
    [Key(1)]
    public PublicKey PublicKey { get; init; } = PublicKey.NULL_PUBLIC_KEY;
    [Key(2)]
    public Address To { get; init; } = Address.NULL_ADDRESS;
    [Key(3)]
    public long Value { get; set; }
    [Key(4)]
    public byte[]? Data { get; init; }
    [Key(5)]
    public long Timestamp { get; init; }
    [Key(6)]
    public Signature Signature { get; set; } = Signature.NULL_SIGNATURE;
    [Key(7)]
    public ExecutionResult ExecutionResult { get; set; }
    [Key(8)]
    public List<Effect> Effects { get; set; } = new();

    [IgnoreMember]
    public Address? From { get => PublicKey.ToAddress(); }

    private SHA256Hash? transactionId;

    public Transaction()
    {

    }

    public Transaction(TransactionDto tx)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("payment requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("payment requires signature");
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey);
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = new Ed25519();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey);
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature);
    }

    public SHA256Hash CalculateHash()
    {
        if (transactionId is not null)
        {
            return transactionId;
        }

        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (PublicKey is not null)
        {
            stream.Write(PublicKey);
        }

        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Flush();
        stream.Position = 0;

        transactionId = sha256.ComputeHash(stream);
        return transactionId;
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)CalculateHash(), (ReadOnlySpan<byte>)(other?.CalculateHash() ?? new byte[0]));
    }
}
