namespace Kryolite.Wallet.Tests;

public class MnemonicTests
{
    [Fact]
    public void Mnemonic_ShouldCreateUniquePhrases()
    {
        var mnemonic1 = Mnemonic.CreateMnemonic();
        var mnemonic2 = Mnemonic.CreateMnemonic();

        Assert.NotEqual(mnemonic1, mnemonic2);
    }

    [Fact]
    public void Mnemonic_ShouldCreateIdenticalMnemonicsWithSameSeeds()
    {
        var seed = new byte[32];
        Array.Fill<byte>(seed, 42);

        var mnemonic1 = Mnemonic.CreateMnemonic(seed);
        var mnemonic2 = Mnemonic.CreateMnemonic(seed);

        Assert.Equal(mnemonic1, mnemonic2);
    }

    [Fact]
    public void Mnemonic_ShouldMnemonicConvertBackToSeed()
    {
        var seed = new byte[32];
        Array.Fill<byte>(seed, 42);

        var mnemonic1 = Mnemonic.CreateMnemonic(seed);

        Mnemonic.TryConvertMnemonicToSeed(mnemonic1, out var seed2);

        Assert.Equal(seed, seed2);
    }
}
