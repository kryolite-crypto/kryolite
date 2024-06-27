using BenchmarkDotNet.Attributes;
using SimpleBase;
using System.Numerics;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class Decoding
{
    private const int N = 37;
    private readonly byte[] data;

    private readonly string base32;
    private readonly string base58;

    public Decoding()
    {
        data = new byte[N];
        new Random(42).NextBytes(data);

        base32 = Base32.ZBase32.Encode(data);
        base58 = Base58.Flickr.Encode(data);
    }

    [Benchmark]
    public byte[] Base32Decode() => Base32.ZBase32.Decode(base32);

    [Benchmark]
    public byte[] Base58Decode() => Base58.Flickr.Decode(base58);
}