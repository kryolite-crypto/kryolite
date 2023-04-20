using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared;

[MessagePackObject]
public class Transaction : IComparable<Transaction>
{
    [IgnoreMember]
    [JsonIgnore]
    public Guid Id { get; set; }
    [IgnoreMember]
    [JsonIgnore]
    public Guid BlockId { get; set; }

    [Key(0)]
    public TransactionType TransactionType { get; set; }
    [Key(1)]
    public PublicKey? PublicKey { get; set; }
    [Key(2)]
    public Address To { get; set; }
    [Key(3)]
    public ulong Value { get; set; }
    [Key(4)]
    public ulong MaxFee { get; set; }
    [Key(5)]
    public byte[]? Data { get; set; }
    [Key(6)]
    public int Nonce { get; set; }
    [Key(7)]
    public Signature? Signature { get; set; }

    [IgnoreMember]
    public List<Effect> Effects { get; set; } = new();
    [IgnoreMember]
    public SHA256Hash Hash { get => CalculateHash(); private set {} }
    [IgnoreMember]
    public Address? From { get => this.PublicKey?.ToAddress(); private set {} }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes((short)TransactionType));
        stream.Write(PublicKey ?? throw new Exception("public key required when signing transactions"));
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(MaxFee));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Nonce));

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes((short)TransactionType));
        stream.Write(PublicKey ?? throw new Exception("public key required when verifying signed transaction (malformed transaction?)"));
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(MaxFee));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Nonce));

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature ?? throw new Exception("trying to verify null signature"));
    }

    public SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(CalculateContentHash());

        if (TransactionType == TransactionType.PAYMENT || TransactionType == TransactionType.CONTRACT) 
        {
            stream.Write(Signature ?? throw new Exception("signature required when hashing payment"));
        }

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    private SHA256Hash CalculateContentHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        if (TransactionType == TransactionType.PAYMENT || TransactionType == TransactionType.CONTRACT) 
        {
            stream.Write(PublicKey ?? throw new Exception("public key required when hashing payment"));
        }

        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(MaxFee));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Nonce));
        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public int CompareTo(Transaction? other)
    {
        return MemoryExtensions.SequenceCompareTo((ReadOnlySpan<byte>)CalculateContentHash(), (ReadOnlySpan<byte>)other!.CalculateContentHash());
    }
}
