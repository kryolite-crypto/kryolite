using System.Security.Cryptography;
using Kryolite.EventBus;
using Kryolite.Shared.Dto;
using MemoryPack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MemoryPackable]
public partial class Transaction : EventBase, IComparable<Transaction>
{
    public TransactionType TransactionType { get; init; }
    public PublicKey PublicKey { get; init; } = PublicKey.NULL_PUBLIC_KEY;
    public Address To { get; set; } = Address.NULL_ADDRESS;
    public ulong Value { get; set; }
    public byte[]? Data { get; init; }
    public long Timestamp { get; init; }
    public Signature Signature { get; set; } = Signature.NULL_SIGNATURE;
    public ExecutionResult ExecutionResult { get; set; }
    public List<Effect> Effects { get; set; } = new();
    public long Id { get; set; }

    [MemoryPackIgnore]
    public Address? From { get => PublicKey.ToAddress(); }

    private bool _isVerified = false;

    [MemoryPackConstructor]
    public Transaction()
    {

    }

    public Transaction(TransactionDto tx)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey ?? throw new Exception("transaction requires public key");
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature ?? throw new Exception("transaction requires signature");
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
        if (_isVerified)
        {
            return true;
        }

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
        
        if(algorithm.Verify(key, stream.ToArray(), Signature))
        {
            _isVerified = true;
            return true;
        }

        return false;
    }

    public SHA256Hash CalculateHash()
    {
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

        return sha256.ComputeHash(stream);
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)CalculateHash(), (ReadOnlySpan<byte>)(other?.CalculateHash() ?? new byte[0]));
    }
}
