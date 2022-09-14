using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Marccacoin.Shared;

public class Transaction : IComparable<Transaction>
{
    public TransactionType TransactionType;
    public PublicKey PublicKey;
    public Address To;
    public ulong Value;
    public ulong MaxFee;
    public byte[]? Data;
    public int Nonce;
    public Signature Signature;

    public Transaction()
    {

    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(((byte)TransactionType)));
        stream.Write(PublicKey);
        stream.Write(To);
        stream.Write(BitConverter.GetBytes(Value));
        stream.Write(BitConverter.GetBytes(MaxFee));
        stream.Write(Data);
        stream.Write(BitConverter.GetBytes(Nonce));

        stream.Flush();

        algorithm.Sign(key, stream.ToArray(), Signature);
    }

    public SHA256Hash CalculateHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(CalculateContentHash());

        if (TransactionType == TransactionType.PAYMENT) {
            stream.Write(Signature);
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
            
            stream.Write(PublicKey);
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