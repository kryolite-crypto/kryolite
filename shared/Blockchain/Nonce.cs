using System.Runtime.InteropServices;
using MessagePack;
using Newtonsoft.Json;
using SimpleBase;

namespace Kryolite.Shared;

[MessagePackObject]
public class Nonce
{
    [Key(0)]
    [JsonProperty]
    public byte[] Buffer { get; private init; }

    public Nonce()
    {
        Buffer = new byte[NONCE_SZ];
    }

    public Nonce(byte[] buffer)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (buffer.Length != NONCE_SZ)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        Buffer = buffer;
    }

    public override string ToString() => Base58.Flickr.Encode(Buffer);
    public static implicit operator byte[] (Nonce nonce) => nonce.Buffer;
    public static implicit operator Nonce (SHA256Hash hash) => new Nonce { Buffer = hash.Buffer };
    public static implicit operator Nonce (byte[] nonce) => new Nonce { Buffer = nonce };
    public static implicit operator Nonce(string nonce) => new Nonce(Base58.Flickr.Decode(nonce));

    public override bool Equals(object? obj) 
    {
        return obj is Nonce c && Enumerable.SequenceEqual(this.Buffer, c.Buffer);
    }

    public static bool operator ==(Nonce a, Nonce b)
    {
        if (System.Object.ReferenceEquals(a, b))
        {
            return true;
        }

        if (((object)a == null) || ((object)b == null))
        {
            return false;
        }

        return a.Equals(b);
    }

    public static bool operator !=(Nonce a, Nonce b)
    {
        return !(a == b);
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var b in Buffer)
        {
            hash = hash * 31 + b.GetHashCode();
        }
        return hash;
    }

    public static int NONCE_SZ = 32;
}
