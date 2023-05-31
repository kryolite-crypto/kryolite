using Kryolite.Shared.Blockchain;
using MessagePack;
using NSec.Cryptography;
using System.ComponentModel.DataAnnotations;
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
    public ulong Value { get; set; }
    [Key(4)]
    [Required]
    public SHA256Hash? Pow { get; set; }
    [Key(5)]
    public byte[]? Data { get; set; }
    [Key(6)]
    public long Timestamp { get; set; }
    [Key(7)]
    [Required]
    public Signature? Signature { get; set; }
    [Key(8)]
    public List<SHA256Hash> Parents { get; set; } = new List<SHA256Hash>();

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.WriteByte((byte)TransactionType);
        stream.Write(PublicKey ?? throw new Exception("public key required when signing transactions"));

        if (To is not null)
        {
            stream.Write(To);
        }

        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Timestamp));

        foreach (var txId in Parents.Order())
        {
            stream.Write(txId);
        }

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
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
        Pow = tx.Pow;
        Data = tx.Data;
        Timestamp = tx.Timestamp;
        Signature = tx.Signature;
        Parents = tx.Parents;
    }
}
