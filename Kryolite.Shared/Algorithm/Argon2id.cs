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
        Span<byte> hash = stackalloc byte[32];
        
        Hash(concat.Buffer, hash);

        return hash;
    }

    public static void Hash(ReadOnlySpan<byte> concat, Span<byte> hash)
    {
        Span<byte> buffer = stackalloc byte[128];
        Argon2id.ComputeHash(buffer, concat, ITERATIONS, MEMORY_SIZE);
        SHA256.TryHashData(buffer, hash, out var _);
    }
}
