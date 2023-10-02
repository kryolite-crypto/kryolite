using System.Security.Cryptography;
using MessagePack;
using NSec.Cryptography;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Vote
{
    public SHA256Hash ViewHash { get; init; } = SHA256Hash.NULL_HASH;
    public PublicKey PublicKey { get; init; } = PublicKey.NULL_PUBLIC_KEY;
    public Signature Signature { get; set; } = Signature.NULL_SIGNATURE;

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(ViewHash);
        stream.Write(PublicKey);

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();

        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        using var stream = new MemoryStream();

        stream.Write(ViewHash);
        stream.Write(PublicKey!);
        stream.Flush();

        Signature = algorithm.Sign(key, stream.ToArray());
    }

    public bool Verify()
    {
        var algorithm = new Ed25519();
        using var stream = new MemoryStream();

        stream.Write(ViewHash);
        stream.Write(PublicKey!);
        stream.Flush();

        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, KeyBlobFormat.RawPublicKey);
        return algorithm.Verify(key, stream.ToArray(), Signature);
    }
}
