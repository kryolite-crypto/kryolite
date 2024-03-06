using System.Diagnostics;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Running;
using Kryolite.Benchmarks;
using Kryolite.Shared;

var a = new ChainState();
a.Weight = new BigInteger(4356456);
Console.WriteLine(a.Weight);
var bytes2 = Serializer.Serialize(a);
var cs = Serializer.Deserialize<ChainState>(bytes2);
Console.WriteLine(cs.Weight);


//BenchmarkRunner.Run<Serialize>();
/*var test = new Serialize();
test.Custom_Serialize();
test.Custom_Deserialize();*/

ref var bytes = ref MemoryMarshal.GetArrayDataReference(new byte[25]);

var sw = Stopwatch.StartNew();
Create2<Final>();
    /*ref var bufRef = ref MemoryMarshal.GetArrayDataReference(address.Buffer);

    Unsafe.CopyBlockUnaligned(
        ref bufRef,
        ref bytes,
        25
    );*/

sw.Stop();
Console.WriteLine(sw.Elapsed.TotalMicroseconds);

Address Create()
{
    return new Address();
}

void Create2<T>() where T: IBase<T>, new()
{
    var final = new T();
    for (var i = 0; i < 1000; i++)
    {
        final.Clone();
    }
}

T Create3<T>(Func<T> func)
{
    return func();
}

public sealed class Final : IBase<Final>
{
    public Address Address = new();

    public Final Clone()
    {
        return new Final();
    }
}

public interface IBase<T>
{
    T Clone();
}