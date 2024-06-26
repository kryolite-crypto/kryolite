using System.Text;
using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Transport.Websocket;

public partial class AuthRequest : ISerializable
{
    public PublicKey PublicKey;
    public long Timestamp;
    public string? PublicUri;
    public int Port;
    public string NetworkName;
    public int ApiLevel;
    public string Version;
    public Signature Signature;

    public AuthRequest()
    {
        PublicKey = new();
        NetworkName = "";
        Signature = new();
        Version = string.Empty;
    }

    public AuthRequest(PublicKey publicKey, Uri? publicUri, int port, string version)
    {
        PublicKey = publicKey;
        PublicUri = publicUri?.ToString();
        Port = port;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Signature = Signature.NULL_SIGNATURE;
        NetworkName = Constant.NETWORK_NAME;
        ApiLevel = Constant.API_LEVEL;
        Version = version;
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new NSec.Cryptography.Ed25519();
        
        using var key = NSec.Cryptography.Key.Import(algorithm, privateKey, NSec.Cryptography.KeyBlobFormat.RawPrivateKey);
        Signature = algorithm.Sign(key, GetBytes());
    }

    public bool Verify()
    {
        // AuthRequest is valid for one minute, should be more than enough to fully propagate it
        var expires = DateTimeOffset.UtcNow.AddMinutes(-1)
            .ToUnixTimeMilliseconds();

        if (Timestamp < expires)
        {
            return false;
        }
        
        var algorithm = new NSec.Cryptography.Ed25519();
        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, NSec.Cryptography.KeyBlobFormat.RawPublicKey);        

        return algorithm.Verify(key, GetBytes(), Signature);
    }

    private byte[] GetBytes()
    {
        var stream = new MemoryStream();
        stream.Write(PublicKey);
        stream.Write(BitConverter.GetBytes(Timestamp));
        stream.Write(Encoding.UTF8.GetBytes(PublicUri ?? string.Empty));
        stream.Write(BitConverter.GetBytes(Port));
        stream.Write(Encoding.UTF8.GetBytes(NetworkName!));
        stream.Write(BitConverter.GetBytes(ApiLevel));
        stream.Write(Encoding.UTF8.GetBytes(Version));
        return stream.ToArray();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.AUTH_REQUEST;
    }

    public int GetLength() =>
        Serializer.SizeOf(PublicKey) +
        Serializer.SizeOf(Timestamp) +
        Serializer.SizeOf(PublicUri) +
        Serializer.SizeOf(Port) +
        Serializer.SizeOf(NetworkName) +
        Serializer.SizeOf(ApiLevel) +
        Serializer.SizeOf(Signature) +
        Serializer.SizeOf(Version);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(Timestamp);
        serializer.Write(PublicUri);
        serializer.Write(Port);
        serializer.Write(NetworkName);
        serializer.Write(ApiLevel);
        serializer.Write(Signature);
        serializer.Write(Version);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref PublicKey);
        serializer.Read(ref Timestamp);
        serializer.ReadN(ref PublicUri);
        serializer.Read(ref Port);
        serializer.Read(ref NetworkName);
        serializer.Read(ref ApiLevel);
        serializer.Read(ref Signature);
        serializer.Read(ref Version);
    }
}
