using BenchmarkDotNet.Attributes;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class Encoding
{
    private const int N = 37;
    private readonly byte[] data;

    public Encoding()
    {
        data = new byte[N];
        new Random(42).NextBytes(data);
    }

    [Benchmark]
    public string Base58() => SimpleBase.Base58.Flickr.Encode(data);

    [Benchmark]
    public string Base32() => SimpleBase.Base32.ZBase32.Encode(data);
}