﻿using BenchmarkDotNet.Attributes;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using NSec.Cryptography;

namespace Kryolite.Benchmarks;

[MemoryDiagnoser]
public class TransactionVerify
{
    private static Wallet wallet = Wallet.Create(WalletType.WALLET);

    private Transaction tx;
    private Ed25519 algorithm;
    private NSec.Cryptography.PublicKey key;
    private byte[] data;
    private byte[] signature;

    public TransactionVerify()
    {
        algorithm = new Ed25519();

        tx = new Transaction
        {
            TransactionType = TransactionType.PAYMENT,
            PublicKey = wallet.PublicKey,
            To = wallet.PublicKey.ToAddress(),
            Value = 1
        };

        tx.Parents.Add(new SHA256Hash());
        tx.Parents.Add(new SHA256Hash());

        tx.Sign(wallet.PrivateKey);

        key = NSec.Cryptography.PublicKey.Import(algorithm, wallet.PublicKey, KeyBlobFormat.RawPublicKey);

        using var stream = new MemoryStream();

        stream.WriteByte((byte)tx.TransactionType);
        stream.Write(tx.PublicKey ?? throw new Exception("public key required when verifying signed transaction (malformed transaction?)"));

        if (tx.To is not null)
        {
            stream.Write(tx.To);
        }

        stream.Write(BitConverter.GetBytes(tx.Value));
        stream.Write(tx.Data);
        stream.Write(BitConverter.GetBytes(tx.Timestamp));

        if (tx.Parents.Count < 2)
        {
            throw new Exception("parent hashes not loaded for transaction");
        }

        foreach (var hash in tx.Parents.Order())
        {
            stream.Write(hash);
        }

        stream.Flush();

        data = stream.ToArray();
        signature = tx.Signature?.Buffer ?? new byte[0];
    }

    [Benchmark]
    public void TxVerify() => tx.Verify();

    [Benchmark]
    public void Verify() => algorithm.Verify(key, data, signature ?? throw new Exception("trying to verify null signature"));
}