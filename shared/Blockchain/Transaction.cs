using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LiteDB;
using NSec.Cryptography;

namespace Marccacoin.Shared;

public class Transaction : IComparable<Transaction>
{
    public long Id { get; set; }
    public TransactionType TransactionType { get; set; }
    public PublicKey? PublicKey { get; set; }
    public Address To { get; set; }
    public ulong Value { get; set; }
    public ulong MaxFee { get; set; }
    public byte[]? Data { get; set; }
    public int Nonce { get; set; }
    public Signature? Signature { get; set; }
    public DateTime ExecutionTime { get; set; }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(((byte)TransactionType)));
        stream.Write(PublicKey ?? throw new Exception("public key required when signing transactions"));
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(MaxFee));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Nonce));

        stream.Flush();

        Signature = new Signature { Buffer = new byte[32] };
        algorithm.Sign(key, stream.ToArray(), Signature.Value);
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