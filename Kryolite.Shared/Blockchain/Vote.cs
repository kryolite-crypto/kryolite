using System.Security.Cryptography;
using Geralt;
using Kryolite.ByteSerializer;
using Kryolite.Type;

namespace Kryolite.Shared.Blockchain;

public sealed class Vote : ISerializable
{
    public SHA256Hash ViewHash;
    public PublicKey PublicKey;
    public Signature Signature;
    public ulong Stake;
    public Address RewardAddress;

    public Vote()
    {
        ViewHash = new();
        PublicKey = new();
        Signature = new();
        RewardAddress = new();
    }

    public SHA256Hash GetHash()
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        stream.Write(ViewHash);
        stream.Write(PublicKey);
        stream.Write(BitConverter.GetBytes(Stake));
        stream.Write(RewardAddress.Buffer);

        stream.Flush();
        stream.Position = 0;

        return sha256.ComputeHash(stream);
    }

    public void Sign(PrivateKey privateKey)
    {
        using var stream = new MemoryStream();

        stream.Write(ViewHash);
        stream.Write(PublicKey!);
        stream.Write(BitConverter.GetBytes(Stake));
        stream.Write(RewardAddress.Buffer);
        stream.Flush();

        var signature = new byte[Signature.SIGNATURE_SZ];
        Ed25519.Sign(signature, stream.ToArray(), privateKey);
        Signature = signature;
    }

    public bool Verify()
    {
        using var stream = new MemoryStream();

        stream.Write(ViewHash);
        stream.Write(PublicKey!);
        stream.Write(BitConverter.GetBytes(Stake));
        stream.Write(RewardAddress.Buffer);
        stream.Flush();

        return Ed25519.Verify(Signature, stream.ToArray(), PublicKey);
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.VOTE;
    }

    public Vote Create<Vote>() where Vote : new()
    {
        return new Vote();
    }

    public int GetLength() =>
        Serializer.SizeOf(ViewHash) +
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(Signature) +
        Serializer.SizeOf(Stake) +
        Serializer.SizeOf(RewardAddress);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(ViewHash);
        serializer.Write(PublicKey);
        serializer.Write(Signature);
        serializer.Write(Stake);
        serializer.Write(RewardAddress);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref ViewHash);
        serializer.Read(ref PublicKey);
        serializer.Read(ref Signature);
        serializer.Read(ref Stake);
        serializer.Read(ref RewardAddress);
    }
}
