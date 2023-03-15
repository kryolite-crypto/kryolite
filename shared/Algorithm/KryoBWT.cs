using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared;

public static class KryoBWT
{
    public static SHA256Hash Hash(Concat concat)
    {
        const int hash_sz = 32;
        const int concat_sz = 64;
        const int load_sz = concat_sz * hash_sz;

        using var sha256 = SHA256.Create();
        using var aes = Aes.Create();

        // round 1
        var extract1 = new byte[load_sz];
        concat.Buffer.CopyTo(extract1, 0);

        for (int i = 0; i < concat_sz; i++)
        {
            var pre = new byte[load_sz + concat_sz];
            extract1.CopyTo(pre, 0);

            pre[load_sz + concat_sz - 1] = concat.Buffer[i];

            var hash = sha256.ComputeHash(pre);

            hash.CopyTo(extract1, i * hash_sz);
        }

        aes.Key = sha256.ComputeHash(concat.Buffer);
        var stage1 = aes.EncryptEcb(extract1, PaddingMode.PKCS7);

        var result1 = Bwt.Transform(stage1);

        // round 2
        var reverse_concat = new byte[concat_sz];
        for (int i = 0; i < concat_sz; i++)
        {
            reverse_concat[i] ^= concat.Buffer[i];
        }

        var extract2 = new byte[load_sz];
        reverse_concat.CopyTo(extract2, 0);

        for (int i = 0; i < concat_sz; i++)
        {
            var pre = new byte[load_sz + concat_sz];
            extract1.CopyTo(pre, 0);

            pre[load_sz + concat_sz - 1] = concat.Buffer[i];

            var hash = sha256.ComputeHash(pre);
            hash.CopyTo(extract1, i * hash_sz);
        }

        aes.Key = sha256.ComputeHash(result1);
        var stage2 = aes.EncryptEcb(extract2, PaddingMode.PKCS7);

        var result2 = Bwt.Transform(stage2);

        var final = new byte[result1.Length + result2.Length];
        result2.CopyTo(final, 0);
        result2.CopyTo(final, result1.Length);

        return sha256.ComputeHash(final);
    }
}
