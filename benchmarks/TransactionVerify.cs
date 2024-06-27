using BenchmarkDotNet.Attributes;
using Geralt;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class TransactionVerify
{
    private readonly Transaction tx;

    public TransactionVerify()
    {
        var wallet = Wallet.Wallet.CreateFromRandomSeed();
        var account = wallet.CreateAccount();

        tx = new Transaction
        {
            TransactionType = TransactionType.PAYMENT,
            PublicKey = account.PublicKey,
            To = account.PublicKey.ToAddress(),
            Value = 1
        };

        var privKey = wallet.GetPrivateKey(account.PublicKey);
        tx.Sign(privKey!);
    }

    [Benchmark]
    public bool Verify() => tx.Verify();
}