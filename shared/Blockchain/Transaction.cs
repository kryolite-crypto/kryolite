using System.Security.Cryptography;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared;

[MessagePackObject]
public class Transaction : IComparable<Transaction>
{
    [IgnoreMember]
    public long Id { get; set; }

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

        Signature = new Signature { Buffer = new byte[64] };
        algorithm.Sign(key, stream.ToArray(), Signature.Value);
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

        var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey.Value, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature ?? throw new Exception("trying to verify null signature"));
    }

    public SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(CalculateContentHash());

        if (TransactionType == TransactionType.PAYMENT) {
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

        if (TransactionType == TransactionType.PAYMENT) {
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