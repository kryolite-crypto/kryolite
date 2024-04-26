using System.Security.Cryptography;
using Kryolite.Shared.Blockchain;
using NSec.Cryptography;

namespace Kryolite.Shared.Algorithm;

public static class Argon2
{
    private static readonly Argon2Parameters _params = new()
    {
        DegreeOfParallelism = 1,
        MemorySize = 8 * 1024,
        NumberOfPasses = 1
    };

    private static readonly Argon2id _algo = PasswordBasedKeyDerivationAlgorithm.Argon2id(_params);

    public static SHA256Hash Hash(Concat concat)
    {
        Span<byte> salt = stackalloc byte[32];

        SHA256.TryHashData(concat.Buffer, salt, out _);

        return _algo.DeriveBytes(concat.Buffer, salt[..16], SHA256Hash.HASH_SZ);
    }

    public static void Hash(ReadOnlySpan<byte> concat, Span<byte> hash)
    {
        Span<byte> salt = stackalloc byte[32];

        SHA256.TryHashData(concat, salt, out _);

        _algo.DeriveBytes(concat, salt[..16], hash);
    }
}
