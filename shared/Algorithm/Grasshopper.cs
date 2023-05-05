using CSChaCha20;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kryolite.Shared;

public static class Grasshopper
{
    public static readonly int MIN_MEM_SZ = 8 * 1024 * 1024;
    public static readonly int MIN_EXP_SZ = 32;
    public static readonly int MIN_ROUNDS = 32;

    public static readonly int MAX_MEM_SZ = 16 * 1024 * 1024;
    public static readonly int MAX_EXP_SZ = 256;
    public static readonly int MAX_ROUNDS = 256;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe static SHA256Hash Hash(Concat concat, Span<byte> sbox, int expSz, int sboxSz, int rounds)
    {
        try
        {
            sbox.Clear();

            using var sha256 = SHA256.Create();

            var tmp = new byte[32];
            var pbox = new Span<byte>(tmp);
            var hash = new Span<byte>(tmp);

            var key = sha256.ComputeHash(concat.Buffer);

            sha256.TryComputeHash(key, hash, out var _);
            using var enc = new ChaCha20(key, hash[0..12], 0);

            sha256.TryComputeHash(hash, hash, out var _);
            var sIX = (int)(new BigInteger(hash, true, false) % (sbox.Length - expSz));

            sha256.TryComputeHash(hash, hash, out var _);
            var tIX = (int)(new BigInteger(hash, true, false) % (sbox.Length - expSz));

            var seed = sbox.Slice(sIX, expSz);
            enc.EncryptBytes(seed, seed);

            for (var i = 0; i < rounds; i++)
            {
                var source = sbox.Slice(sIX, expSz);
                var target = sbox.Slice(tIX, expSz);

                sha256.TryComputeHash(source, hash, out var _);

                var work = new BigInteger(hash, true, false);
                var op = (int)(work % 11);

                i += (int)BigInteger.LeadingZeroCount(work);

                switch (op)
                {
                    case 0:
                        enc.EncryptBytes(source, target);
                        break;
                    case 1:
                        Splash32(sbox, expSz, sha256, hash);
                        enc.EncryptBytes(source, target);
                        break;
                    case 2:
                        Splash64(sbox, expSz, sha256, hash);
                        enc.EncryptBytes(source, target);
                        break;
                    case 3:
                        ExpandToTarget(sha256, expSz, pbox, source, target);
                        enc.EncryptBytes(target, target);
                        break;
                    case 4:
                        BWT.Encode(source, target, out _);
                        break;
                    case 5:
                        Splash32(sbox, expSz, sha256, hash);
                        BWT.Encode(source, target, out _);
                        break;
                    case 6:
                        Splash64(sbox, expSz, sha256, hash);
                        BWT.Encode(source, target, out _);
                        break;
                    case 7:
                        ExpandToTarget(sha256, expSz, pbox, source, target);
                        BWT.Encode(target, target, out _);
                        break;
                    case 8:
                        Splash32(sbox, expSz, sha256, hash);
                        ExpandToTarget(sha256, expSz, pbox, source, target);
                        break;
                    case 9:
                        Splash64(sbox, expSz, sha256, hash);
                        ExpandToTarget(sha256, expSz, pbox, source, target);
                        break;
                    default:
                        ExpandToTarget(sha256, expSz, pbox, source, target);
                        break;
                }

                sIX = tIX;
                tIX = (int)(new BigInteger(target, true, false) % (sbox.Length - expSz));
            }

            sha256.TryComputeHash(sbox, hash, out var _);

            return hash;
        }
        catch (Exception ex)
        {
            throw new Exception($"hash failure, nonce [{string.Join("-", concat.Buffer)}]", ex);
        }
    }

    private static unsafe void Splash64(Span<byte> sbox, int expSz, SHA256 sha256, Span<byte> hash)
    {
        sha256.TryComputeHash(hash, hash, out var _);

        for (int x = 0; x < hash.Length; x += sizeof(long))
        {
            var ix = (int)(BitConverter.ToInt64(hash.ToArray(), x) % (sbox.Length - expSz));

            if (ix < 0)
            {
                ix = -ix;
            }

            sbox[ix]++;
        }
    }

    private static unsafe void Splash32(Span<byte> sbox, int expSz, SHA256 sha256, Span<byte> hash)
    {
        sha256.TryComputeHash(hash, hash, out var _);

        for (int x = 0; x < hash.Length; x += sizeof(int))
        {
            var ix = BitConverter.ToInt32(hash.ToArray(), x) % (sbox.Length - expSz);

            if (ix < 0)
            {
                ix = -ix;
            }

            sbox[ix]++;
        }
    }

    private static unsafe void ExpandToTarget(SHA256 sha256, int expSz, Span<byte> pbox, Span<byte> source, Span<byte> target)
    {
        for (int x = 0; x < expSz - 1; x += 2)
        {
            sha256.TryComputeHash(source, pbox, out var _);

            var mOp = (int)(new BigInteger(pbox, true, false) % 16);

            var a = source[x];
            var b = source[x + 1];

            if (mOp < 1) a = (byte)((a ^ b) ^ pbox[0]);
            if (mOp < 2) b = (byte)((b ^ a) ^ pbox[1]);
            if (mOp < 3) a = (byte)((a ^ b) ^ pbox[2]);
            if (mOp < 4) b = (byte)((b ^ a) ^ pbox[3]);
            if (mOp < 5) a = (byte)((a ^ b) ^ pbox[4]);
            if (mOp < 6) b = (byte)((b ^ a) ^ pbox[5]);
            if (mOp < 7) a = (byte)((a ^ b) ^ pbox[6]);
            if (mOp < 8) b = (byte)((b ^ a) ^ pbox[7]);
            if (mOp < 9) a = (byte)((a ^ b) ^ pbox[8]);
            if (mOp < 10) b = (byte)((b ^ a) ^ pbox[9]);
            if (mOp < 11) a = (byte)((a ^ b) ^ pbox[10]);
            if (mOp < 12) b = (byte)((b ^ a) ^ pbox[11]);
            if (mOp < 13) a = (byte)((a ^ b) ^ pbox[12]);
            if (mOp < 14) b = (byte)((b ^ a) ^ pbox[13]);
            if (mOp < 15) a = (byte)((a ^ b) ^ pbox[14]);
            if (mOp < 16) b = (byte)((b ^ a) ^ pbox[15]);

            target[x] = a;
            target[x + 1] = b;
        }
    }

    public unsafe static SHA256Hash Hash(SHA256Hash parenthash, Concat concat)
    {
        GetBlockFeatures(parenthash, out var expSz, out var sboxSz, out var rounds);

        var mem = Marshal.AllocHGlobal(sboxSz);

        try
        {
            var scratchpad = new Span<byte>(mem.ToPointer(), sboxSz);
            var hash = Hash(concat, scratchpad, expSz, sboxSz, rounds);
            
            return hash;
        }
        catch (Exception ex)
        {
            throw new Exception("hash failure", ex);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    public static void GetBlockFeatures(SHA256Hash parentHash, out int expSz, out int sboxSz, out int rounds)
    {
        var rules = SHA256.HashData(parentHash);

        expSz  = MAX_EXP_SZ - (int)(BitConverter.ToUInt32(rules, 0) % (MAX_EXP_SZ - MIN_EXP_SZ));
        sboxSz = MAX_MEM_SZ - (int)(BitConverter.ToUInt32(rules, 4) % (MAX_MEM_SZ - MIN_MEM_SZ));
        rounds = MAX_ROUNDS - (int)(BitConverter.ToUInt32(rules, 8) % (MAX_ROUNDS - MIN_ROUNDS));
    }
}
