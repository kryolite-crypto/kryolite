using System.Diagnostics;
using BenchmarkDotNet.Running;
using Kryolite.Benchmarks;

//BenchmarkRunner.Run<TransactionVerify>();

var tx = new TransactionVerify();
var sw = Stopwatch.StartNew();;
for (var i = 0; i < 1_000_000; i++)
{
    tx.Verify();
}
sw.Stop();
Console.WriteLine(1_000_000 / sw.Elapsed.TotalSeconds);