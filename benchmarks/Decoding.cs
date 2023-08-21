using BenchmarkDotNet.Attributes;
using SimpleBase;
using System.Numerics;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class Decoding
{
    private const int N = 32;
    private readonly byte[] data;

    private readonly string base32;
    private readonly string base58;

    private static Base32 KryoliteBase = new Base32(new Base32Alphabet("abcdefghijkmnpqrstuvwxyz23456789"));

    public Decoding()
    {
        data = new byte[N];
        new Random(42).NextBytes(data);

        base32 = KryoliteBase.Encode(data);
        base58 = SimpleBase.Base58.Flickr.Encode(data);
    }

    [Benchmark]
    public byte[] Base32Decode() => KryoliteBase.Decode(base32);

    [Benchmark]
    public byte[] Base58Decode() => SimpleBase.Base58.Flickr.Decode(base58);
}