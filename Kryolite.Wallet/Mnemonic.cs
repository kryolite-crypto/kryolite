using System.Collections;
using System.Security.Cryptography;

namespace Kryolite.Wallet;

public static class Mnemonic
{
    public static string CreateMnemonic(byte[]? seed = null)
    {
        if (seed is null)
        {
            seed = new byte[33];
            Random.Shared.NextBytes(seed);
        }

        if (seed.Length != 33)
        {
            var resized = new byte[33];
            seed.CopyTo(resized, 0);
            seed = resized;
        }

        var checksum = SHA256.HashData(seed.AsSpan()[0..32]);
        seed[32] = checksum[0];

        var bits = new BitArray(seed);
        Span<int> values = stackalloc int[24];

        for (var i = 0; i < bits.Length; i++)
        {
            var bit = bits[i];
            values[i / 11] += bit ? 1 << (10 - (i % 11)) : 0;
        }

        var wordList = WordList.Words;
        var words = new string[24];

        for (var i = 0; i < words.Length; i++)
        {
            words[i] = wordList[values[i]];
        }

        var mnemonic = string.Join(' ', words);

        if (!TryConvertMnemonicToSeed(mnemonic, out var seed2))
        {
            throw new Exception("failed to verify mnemonic");
        }

        if (!Enumerable.SequenceEqual(seed[0..32], seed2))
        {
            throw new Exception("mnemonic to seed does not match");
        }

        return mnemonic;
    }

    public static bool TryConvertMnemonicToSeed(string mnemonic, out byte[] seed)
    {
        var words = mnemonic.Trim()
            .Split(' ')
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();

        if (words.Length != 24)
        {
            seed = [];
            return false;
        }

        var wordList = WordList.Words;
        var values = new int[24];

        for (var i = 0; i < 24; i++)
        {
            values[i] = Array.IndexOf(wordList, words[i]);
        }

        var bits = new BitArray(words.Length * 11);
        var bi = 0;

        foreach (var value in values)
        {
            for (var x = 0; x < 11; x++)
            {
                bits.Set(bi, (value & ( 1 << (10 - x))) != 0);
                bi++;
            }
        }

        seed = new byte[33];
        bits.CopyTo(seed, 0);

        var checksum = SHA256.HashData(seed.AsSpan()[0..32]);

        if (seed[32] != checksum[0])
        {
            return false;
        }

        seed = seed[0..32];
        return true;
    }
}