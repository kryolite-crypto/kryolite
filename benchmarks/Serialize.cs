using BenchmarkDotNet.Attributes;
using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class Serialize
{
    public Transaction _tx;
    private byte[] _custom;

    public Serialize()
    {
        _tx = new Transaction
        {
            Id = 11,
            Value = 500,
            Timestamp = 564564
        };

        Array.Fill<byte>(_tx.PublicKey.Buffer, 69);
        Array.Fill<byte>(_tx.Data, 42);
        Array.Fill<byte>(_tx.To.Buffer, 12);
        Array.Fill<byte>(_tx.Signature.Buffer, 55);

        for (var i = 0; i < 1000; i++)
        {
            _tx.Effects.Add(new Effect());
        }

        _custom = Custom_Serialize();
    }

    [Benchmark]
    public byte[] Custom_Serialize()
    {
        return Serializer.Serialize(_tx);
    }

    [Benchmark]
    public Transaction? Custom_Deserialize()
    {
        return Serializer.Deserialize<Transaction>(_custom);
    }

}