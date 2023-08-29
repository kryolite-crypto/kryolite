using Kryolite.Shared.Blockchain;
using MessagePack;
using NSec.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using KeyAttribute = MessagePack.KeyAttribute;

namespace Kryolite.Shared.Dto;

[MessagePackObject]
public class TransactionDto
{
    [Key(0)]
    public TransactionType TransactionType { get; set; }
    [Key(1)]
    [Required]
    public PublicKey? PublicKey { get; set; }
    [Key(2)]
    [Required]
    public Address? To { get; set; }
    [Key(3)]
    public long Value { get; set; }
    [Key(4)]
    public byte[]? Data { get; set; }
    [Key(5)]
    public long Timestamp { get; set; }
    [Key(6)]
    [Required]
    public Signature? Signature { get; set; }
    [Key(7)]
    public List<SHA256Hash> Parents { get; set; } = new List<SHA256Hash>();

    public SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);

        if (TransactionType == TransactionType.VIEW || TransactionType == TransactionType.PAYMENT || TransactionType == TransactionType.CONTRACT)
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

        foreach (var hash in Parents.Order())
        {
            stream.Write(hash);
        }

        return sha256.ComputeHash(stream.ToArray());
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
        Parents = tx.Parents;
    }

    public Transaction AsTransaction()
    {
        switch (TransactionType)
        {
            case TransactionType.BLOCK:
                return new Block(this, Parents);
            case TransactionType.PAYMENT:
            case TransactionType.CONTRACT:
                return new Transaction(this, Parents);
            case TransactionType.VIEW:
                return new View(this, Parents);
            case TransactionType.VOTE:
                return new Vote(this, Parents);
            default:
                throw new Exception($"Transaction type {TransactionType}");
        }
    }
}
