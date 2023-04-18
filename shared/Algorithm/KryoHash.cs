using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Kryolite.Shared;

public static class KryoHash
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe static SHA256Hash Hash(Concat concat)
    {
        uint a = 0, b = 0;
        var sbox = new byte[1024 * 512];
        int subdiff = 32;

        var tag = new byte[16];
        var key = SHA256.HashData(concat.Buffer);
        var nonce = SHA256.HashData(key)[0..12];

        var enc = new ChaCha20Poly1305(key);
        enc.Encrypt(nonce, sbox, sbox, tag);

        do
        {
            for (var i = 0; i < sbox.Length; i += 2)
            {
                a ^= sbox[i] ^ (uint)tag[0];
                b ^= sbox[i + 1];

                for (var x = 0; x < tag.Length; x += 2)
                {
                    var op = a ^ b;
                    var t1 = tag[x];
                    var t2 = tag[x + 1];

                    // TODO: verify op distribution
                    if (op <= 85)
                    {
                        a = (a ^ b) ^ t1;
                        b = (b ^ a) ^ t2;
                    }
                    else if (op <= 170)
                    {
                        a = (a & b) ^ t1;
                        b = (b & a) ^ t2;
                    }
                    else
                    {
                        a = (a | b) ^ t1;
                        b = (b | a) ^ t2;
                    }
                }

                sbox[i] = (byte)b;
                sbox[i + 1] = (byte)a;
            }

        } while (sbox[sbox.Length - 1] < --subdiff);

        return SHA256.HashData(sbox);
    }
}
