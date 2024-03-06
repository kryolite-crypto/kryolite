using System.Text;
using Kryolite.Shared;
using NSec.Cryptography;

namespace Kryolite.Grpc.NodeService;

public partial class AuthRequest : ISerializable
{
    public Shared.PublicKey PublicKey;
    public long Timestamp;
    public string? PublicUri;
    public int Port;
    public string NetworkName;
    public int ApiLevel;
    public Signature Signature;
    public long Challenge;

    public AuthRequest()
    {
        PublicKey = new();
        NetworkName = "";
        Signature = new();
    }

    public AuthRequest(Shared.PublicKey publicKey, Uri? publicUri, int port, long challenge)
    {
        PublicKey = publicKey;
        PublicUri = publicUri?.ToString();
        Port = port;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Signature = Signature.NULL_SIGNATURE;
        NetworkName = Constant.NETWORK_NAME;
        ApiLevel = Constant.API_LEVEL;
        Challenge = challenge;
    }

    public void Sign(PrivateKey privateKey)
    {
        var algorithm = new Ed25519();
        
        using var key = Key.Import(algorithm, privateKey, KeyBlobFormat.RawPrivateKey);
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
        
        var algorithm = new Ed25519();
        var key = NSec.Cryptography.PublicKey.Import(algorithm, PublicKey, KeyBlobFormat.RawPublicKey);        

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
        Serializer.SizeOf(Challenge);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(PublicKey);
        serializer.Write(Timestamp);
        serializer.Write(PublicUri);
        serializer.Write(Port);
        serializer.Write(NetworkName);
        serializer.Write(ApiLevel);
        serializer.Write(Signature);
        serializer.Write(Challenge);
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
        serializer.Read(ref Challenge);
    }
}
