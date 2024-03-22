using Kryolite.ByteSerializer;
using Kryolite.Shared;
using NSec.Cryptography;

namespace Kryolite.Transport.Websocket;

public class AuthResponse : ISerializable
{
    public Shared.PublicKey PublicKey;
    public long Nonce;
    public Signature Signature;

    public AuthResponse()
    {
        PublicKey = new();
        Signature = new();
    }

    public AuthResponse(Shared.PublicKey publicKey, long nonce)
    {
        PublicKey = publicKey;
        Nonce = nonce;
        Signature = Signature.NULL_SIGNATURE;
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();
        
        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
        Signature = algorithm.Sign(key, GetBytes());
    }

    public bool Verify()
    {        
        var algorithm = new Ed25519();
        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, KeyBlobFormat.RawPublicKey);        

        return algorithm.Verify(key, GetBytes(), Signature);
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.AUTH_RESPONSE;
    }

    public int GetLength() =>
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(Nonce) +
        Serializer.SizeOf(Signature);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(Nonce);
        serializer.Write(Signature);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PublicKey);
        serializer.Read(ref Nonce);
        serializer.Read(ref Signature);
    }

    private byte[] GetBytes()
    {
        var stream = new MemoryStream();
        stream.Write(PublicKey);
        stream.Write(BitConverter.GetBytes(Nonce));
        return stream.ToArray();
    }
}
