using System.Text.Json.Serialization;
using DuckDB.NET.Data;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Vote
{
    [Key(0)]
    public SHA256Hash TransactionId { get; set; } = new SHA256Hash();

    [Key(1)]
    public Shared.PublicKey PublicKey { get; set; } = new PublicKey();

    [Key(2)]
    public Signature Signature { get; set; } = new Signature();

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(TransactionId);
        stream.Write(PublicKey);

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var stream = new MemoryStream();

        stream.Write(TransactionId);
        stream.Write(PublicKey);

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature);
    }

    public static Vote Read(DuckDBDataReader reader)
    {
        return new Vote
        {
            Signature = reader.GetString(0),
            PublicKey = reader.GetString(1),
            TransactionId = reader.GetString(2),
        };
    }
}
