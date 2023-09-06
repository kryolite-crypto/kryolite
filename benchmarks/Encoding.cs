using BenchmarkDotNet.Attributes;
using SimpleBase;
using System.Numerics;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class Encoding
{
    private const int N = 32;
    private readonly byte[] data;

    private static Base32 KryoliteBase = new Base32(new Base32Alphabet("abcdefghijkmnpqrstuvwxyz23456789"));

    public Encoding()
    {
        data = new byte[N];
        new Random(42).NextBytes(data);
    }

    [Benchmark]
    public string Base32Encode() => KryoliteBase.Encode(data);

    [Benchmark]
    public string Base58Encode() => SimpleBase.Base58.Flickr.Encode(data);

    [Benchmark]
    public string HexEncode() => BitConverter.ToString(data);
}