using System.Runtime.Serialization;
using System.Text;
using Kryolite.Shared;
using MemoryPack;
using NSec.Cryptography;

namespace Kryolite.Grpc.NodeService;

[DataContract]
[MemoryPackable]
public partial class AuthRequest
{
    [DataMember]
    public Kryolite.Shared.PublicKey PublicKey { get; set; }
    [DataMember]
    public long Timestamp { get; set; }
    [DataMember]
    public string? PublicUri { get; set; }
    [DataMember]
    public int Port { get; set; }
    [DataMember]
    public string NetworkName { get; set; }
    [DataMember]
    public int ApiLevel { get; set; }
    [DataMember]
    public Signature Signature { get; set; }

    public AuthRequest(Kryolite.Shared.PublicKey publicKey, Uri? publicUri, int port)
    {
        PublicKey = publicKey;
        PublicUri = publicUri?.ToString();
        Port = port;
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Signature = Signature.NULL_SIGNATURE;
        NetworkName = Constant.NETWORK_NAME;
        ApiLevel = Constant.API_LEVEL;
    }

    [MemoryPackConstructor]
    public AuthRequest(Kryolite.Shared.PublicKey publicKey, long timestamp, string? publicUri, int port, Signature signature, string networkName, int apiLevel)
    {
        PublicKey = publicKey;
        Timestamp = timestamp;
        PublicUri = publicUri;
        Port = port;
        Signature = signature;
        NetworkName = networkName;
        ApiLevel = apiLevel;
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
        stream.Write(Encoding.UTF8.GetBytes(NetworkName.ToString()));
        stream.Write(BitConverter.GetBytes(ApiLevel));
        return stream.ToArray();
    }
}
