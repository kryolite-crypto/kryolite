using System.Numerics;
using NSec.Cryptography;

namespace Kryolite.Wallet.Tests;

public class WalletTests
{
    [Fact]
    public void Wallet_CreateSeed()
    {
        var seed = new byte[32];
        Array.Fill<byte>(seed, 42);

        var wallet1 = Wallet.CreateFromSeed(seed);
        var account1_1 = wallet1.CreateAccount();
        var account1_2 = wallet1.CreateAccount();

        var wallet2 = Wallet.CreateFromSeed(seed);
        var account2_1 = wallet2.CreateAccount();
        var account2_2 = wallet2.CreateAccount();

        // private keys should be equal
        Assert.Equal(wallet1.PrivateKey, wallet2.PrivateKey);

        // Should have same addresses
        Assert.Equal(account1_1.Address, account2_1.Address);
        Assert.Equal(account1_1.PublicKey, account2_1.PublicKey);

        Assert.Equal(account1_2.Address, account2_2.Address);
        Assert.Equal(account1_2.PublicKey, account2_2.PublicKey);
    }

    [Fact]
    public void Wallet_CreateFromRandomSeed()
    {
        var wallet1 = Wallet.CreateFromRandomSeed();
        var account1 = wallet1.CreateAccount();

        var wallet2 = Wallet.CreateFromRandomSeed();
        var account2 = wallet2.CreateAccount();

        Assert.NotEqual(wallet1.PrivateKey, wallet2.PrivateKey);

        // Check that chaincode increased
        Assert.Equal(1U, wallet1.ChainCode);
        Assert.Equal(1U, wallet2.ChainCode);

        // Should have different address
        Assert.NotEqual(account1.Address, account2.Address);
        Assert.NotEqual(account1.PublicKey, account2.PublicKey);
    }

    [Fact]
    public void Wallet_CreateAccount()
    {
        var wallet = Wallet.CreateFromRandomSeed();

        var account1 = wallet.CreateAccount();
        var account2 = wallet.CreateAccount();

        Assert.NotEqual(account1.Id, account2.Id);
        Assert.NotEqual(account1.PublicKey, account2.PublicKey);
        Assert.NotEqual(account1.Address, account2.Address);

        Assert.NotNull(wallet.GetAccount(account1.Address));
        Assert.NotNull(wallet.GetAccount(account1.PublicKey));

        Assert.NotNull(wallet.GetAccount(account2.Address));
        Assert.NotNull(wallet.GetAccount(account2.PublicKey));

        Assert.Equal(2U, wallet.ChainCode);
    }

    [Fact]
    public void Wallet_CreateAccount_SignAndVerify()
    {
        var wallet = Wallet.CreateFromRandomSeed();
        var account1 = wallet.CreateAccount();
        var account2 = wallet.CreateAccount();

        var data = new byte[32];
        
        Array.Fill<byte>(data, 42);

        var key1 = Key.Import(Ed25519.Ed25519, wallet.GetPrivateKey(account1.PublicKey)!, KeyBlobFormat.RawPrivateKey);
        var key2 = Key.Import(Ed25519.Ed25519, wallet.GetPrivateKey(account2.Address)!, KeyBlobFormat.RawPrivateKey);
        var signature1 = Ed25519.Ed25519.Sign(key1, data);
        var signature2 = Ed25519.Ed25519.Sign(key2, data);

        var pubKey1 = PublicKey.Import(Ed25519.Ed25519, account1.PublicKey, KeyBlobFormat.RawPublicKey);
        var pubKey2 = PublicKey.Import(Ed25519.Ed25519, account2.PublicKey, KeyBlobFormat.RawPublicKey);
        
        // signatures should be valid
        Assert.True(Ed25519.Ed25519.Verify(pubKey1, data, signature1));
        Assert.True(Ed25519.Ed25519.Verify(pubKey2, data, signature2));

        // and they should be different
        Assert.NotEqual(signature1, signature2);
    }
}