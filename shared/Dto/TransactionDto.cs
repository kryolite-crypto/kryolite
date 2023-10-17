using Kryolite.EventBus;
using Kryolite.Shared.Blockchain;
using MessagePack;
using NSec.Cryptography;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using KeyAttribute = MessagePack.KeyAttribute;

namespace Kryolite.Shared.Dto;

[MessagePackObject]
public class TransactionDto : EventBase
{
    [Key(0)]
    public TransactionType TransactionType { get; init; }
    [Key(1)]
    [Required]
    public PublicKey PublicKey { get; init; } = PublicKey.NULL_PUBLIC_KEY;
    [Key(2)]
    [Required]
    public Address To { get; init; } = Address.NULL_ADDRESS;
    [Key(3)]
    public ulong Value { get; init; }
    [Key(4)]
    public byte[]? Data { get; init; }
    [Key(5)]
    public long Timestamp { get; init; }
    [Key(6)]
    [Required]
    public Signature Signature { get; init; } = Signature.NULL_SIGNATURE;
    [IgnoreMember]
    public bool IsValid { get; set; }

    private SHA256Hash? CachedTransactionId { get; set; }

    public SHA256Hash CalculateHash(bool forceRefresh = false)
    {
        if (CachedTransactionId is not null && !forceRefresh)
        {
            return CachedTransactionId;
        }

        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (PublicKey is not null)
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
        stream.Flush();
        stream.Position = 0;

        CachedTransactionId = sha256.ComputeHash(stream.ToArray());

        return CachedTransactionId;
    }

    public TransactionDto()
    {

    }

    public TransactionDto(Transaction tx)
    {
        TransactionType = tx.TransactionType;
        PublicKey = tx.PublicKey;
        To = tx.To;
        Value = tx.Value;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature;
    }
}
