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
        const int multiplier = 1;

        var mine = new byte[load_sz * multiplier * 2];
        var explode = new byte[mine.Length + 1];

        using var sha256 = SHA256.Create();
        using var aes = Aes.Create();

        // round 1
        var extract1 = new Span<byte>(mine, 0, load_sz * multiplier);
        concat.Buffer.CopyTo(extract1);

        for (int i = 0; i < (concat_sz * multiplier); i++)
        {
            extract1.CopyTo(explode);
            explode[load_sz + concat_sz - 1] = concat.Buffer[i % concat_sz];

            sha256.TryComputeHash(explode, extract1.Slice(i * hash_sz), out _);
        }

        aes.Key = sha256.ComputeHash(concat.Buffer);
        var stage1 = aes.EncryptEcb(extract1, PaddingMode.None);

        var result1 = Bwt.Transform(stage1);

        // prepare round 2
        var reverse_concat = new byte[concat_sz];
        for (int i = 0; i < concat_sz; i++)
        {
            reverse_concat[i] ^= concat.Buffer[i];
        }

        // round 2
        var extract2 = new Span<byte>(mine, extract1.Length, load_sz * multiplier);
        reverse_concat.CopyTo(extract2);

        for (int i = 0; i < (concat_sz * multiplier); i++)
        {
            extract2.CopyTo(explode);
            explode[load_sz + concat_sz - 1] = concat.Buffer[i % concat_sz];

            var hash = sha256.ComputeHash(explode);

            sha256.TryComputeHash(explode, extract2.Slice(i * hash_sz), out _);
        }

        aes.Key = sha256.ComputeHash(result1);
        var stage2 = aes.EncryptEcb(extract2, PaddingMode.None);

        var result2 = Bwt.Transform(stage2);

        // finalize
        var final_key = new byte[result1.Length + result2.Length];
        result1.CopyTo(final_key, 0);
        result2.CopyTo(final_key, result1.Length);

        aes.Key = sha256.ComputeHash(final_key);
        var final = aes.EncryptEcb(mine, PaddingMode.None);

        return sha256.ComputeHash(final);
    }
}
