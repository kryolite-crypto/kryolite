using System.Runtime.InteropServices;
using NSec.Cryptography;

namespace Marccacoin;

public class Transaction
{
    public TransactionType TransactionType;
    public PublicKey PublicKey;
    public Address To;
    public ulong Value;
    public ulong MaxFee;
    public byte[]? Data;
    public int Nonce;
    public Signature Signature;

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
}