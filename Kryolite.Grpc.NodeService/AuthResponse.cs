using System.Runtime.Serialization;
using Kryolite.Shared;
using MemoryPack;
using NSec.Cryptography;

namespace Kryolite.Grpc.NodeService;

[DataContract]
[MemoryPackable]
public partial class AuthResponse
{
    [DataMember]
    public Shared.PublicKey PublicKey { get; set; }
    [DataMember]
    public long Nonce { get; set; }
    [DataMember]
    public Signature Signature { get; set; }

    public AuthResponse(Shared.PublicKey publicKey, long nonce)
    {
        PublicKey = publicKey;
        Nonce = nonce;
        Signature = Signature.NULL_SIGNATURE;
    }

    [MemoryPackConstructor]
    public AuthResponse(Shared.PublicKey publicKey, long nonce, Signature signature)
    {
        PublicKey = publicKey;
        Nonce = nonce;
        Signature = signature;
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

    private byte[] GetBytes()
    {
        var stream = new MemoryStream();
        stream.Write(PublicKey);
        stream.Write(BitConverter.GetBytes(Nonce));
        return stream.ToArray();
    }
}
