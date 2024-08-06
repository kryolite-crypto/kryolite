using System.Security.Cryptography;
using Geralt;
using Kryolite.Type;

namespace Kryolite.Shared.Algorithm;

public static class Argon2
{
    private const int ITERATIONS = 1;
    private const int MEMORY_SIZE = 4 * 1024 * 1024;

    public static SHA256Hash Hash(Concat concat)
    {
        var hash = new byte[32];
        Hash(concat, hash);
        return hash;
    }

    public static void Hash(ReadOnlySpan<byte> concat, Span<byte> hash)
    {
        Span<byte> buffer = stackalloc byte[Argon2id.MaxHashSize];
        Span<byte> salt = stackalloc byte[32];

        SHA256.TryHashData(concat, salt, out _);
        Argon2id.DeriveKey(buffer, concat, salt[0..16], ITERATIONS, MEMORY_SIZE);
        SHA256.TryHashData(buffer, hash, out _);
    }
}
