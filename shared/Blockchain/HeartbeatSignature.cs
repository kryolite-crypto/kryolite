using System.Text.Json.Serialization;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class HeartbeatSignature
{
    [IgnoreMember]
    [JsonIgnore]
    public long Id { get; set; }

    [Key(0)]
    public long Height { get; set; }

    [Key(1)]
    public SHA256Hash TransactionId { get; set; } = new SHA256Hash();

    [Key(2)]
    public Shared.PublicKey PublicKey { get; set; } = new PublicKey();

    [Key(3)]
    public Signature Signature { get; set; } = new Signature();

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Height));
        stream.Write(TransactionId);
        stream.Write(PublicKey);

        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = SignatureAlgorithm.Ed25519;

        using var stream = new MemoryStream();

        stream.Write(BitConverter.GetBytes(Height));
        stream.Write(TransactionId);
        stream.Write(PublicKey);

        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature);
    }
}
