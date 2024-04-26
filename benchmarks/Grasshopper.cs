using BenchmarkDotNet.Attributes;
using Kryolite.Shared;
using Kryolite.Shared.Algorithm;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public unsafe partial class GrasshopperHash
{
    private Concat concat;

    public GrasshopperHash()
    {
        concat = new();
        new Random(42).NextBytes(concat.Buffer);
    }

    [Benchmark]
    public byte[] Hash()
    {
        return (byte[])Grasshopper.Hash(concat);
    }
}
