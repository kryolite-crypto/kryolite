using System.Collections;
using System.Numerics;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Kryolite.Shared;
using Kryolite.Shared.Algorithm;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public unsafe partial class GrasshopperHash
{
    private Concat concat;
    private string c;
    private object b;

    public GrasshopperHash()
    {
        concat = new();
        new Random(42).NextBytes(concat.Buffer);
        b = Random.Shared.Next().ToString();
        c = (string)b;
    }

    [Benchmark]
    public byte[] Hash()
    {
        return Grasshopper.Hash(concat);
    }
}
