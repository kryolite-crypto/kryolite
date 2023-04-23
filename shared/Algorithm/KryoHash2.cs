using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Kryolite.Shared;

public static class KryoHash2
{
    public static readonly int MEM_MULTIPLIER = 8;
    public static readonly int EXP_MULTIPLIER = 1024;
    public static readonly int ROUND_MULTIPLIER = 8;
    public static readonly int MAX_MEM = MEM_MULTIPLIER * 1024 * 1024;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe static SHA256Hash Hash(Concat concat, byte[] scratchpad)
    {
        Array.Clear(scratchpad);

        var rules = SHA256.HashData(concat.Buffer[0..32]);

        int expSz = Math.Max(rules[0] % EXP_MULTIPLIER, 32);
        int sboxSz = Math.Max(rules[1] % MEM_MULTIPLIER, (byte)1) * 1024 * 1024;
        int rounds = Math.Max(rules[2] % ROUND_MULTIPLIER, (byte)1) * 1024;

        var sbox = scratchpad.AsSpan().Slice(0, sboxSz);

        var iv = (new byte[32]).AsSpan();
        var nonce = iv.Slice(0, 12);

        var keyHash = SHA256.HashData(concat.Buffer);

        var enc = new NSec.Cryptography.ChaCha20Poly1305();
        using var key = NSec.Cryptography.Key.Import(enc, keyHash, NSec.Cryptography.KeyBlobFormat.RawSymmetricKey);

        SHA256.TryHashData(keyHash, iv, out var _);

        var cIX = 0;
        var nIX = BitConverter.ToInt32(nonce[0..4]) % (sbox.Length - expSz);

        var payloadAndTag = new byte[expSz + enc.TagSize].AsSpan();
        var payload = payloadAndTag.Slice(0, expSz);
        var tag = payloadAndTag.Slice(expSz, enc.TagSize);

        enc.Encrypt(key, nonce, null, payload, payloadAndTag);

        nonce = tag.Slice(0, 12);

        for (var i = 0; i < rounds; i++)
        {
            if (nIX < 0)
            {
                nIX = -nIX;
                continue;
            }

            var source = sbox.Slice(cIX, expSz);
            var target = sbox.Slice(nIX, expSz);

            for (int x = 0; x < expSz - 1; x += 2)
            {
                var mOp = SHA256.HashData(payload)[0] % 24;

                var a = source[x];
                var b = source[x + 1];

                switch (mOp)
                {
                    case 0:
                        payload[x] = (byte)(a ^ b);
                        payload[x + 1] = (byte)(a & b);
                        break;
                    case 1:
                        payload[x] = (byte)(a & b);
                        payload[x + 1] = (byte)(a | b);
                        break;
                    case 2:
                        payload[x] = (byte)(a | b);
                        payload[x + 1] = (byte)(a - b);
                        break;
                    case 3:
                        payload[x] = (byte)(a - b);
                        payload[x + 1] = (byte)(a + b);
                        break;
                    case 4:
                        payload[x] = (byte)(a + b);
                        payload[x + 1] = (byte)(a * b);
                        break;
                    case 5:
                        payload[x] = (byte)(a * b);
                        payload[x + 1] = (byte)(a ^ b);
                        break;
                    default:
                        i++;
                        continue;
                }
            }

            var op = BitConverter.ToUInt32(payload[0..4]) % 4;

            if (op == 0)
            {
                var result = new byte[payloadAndTag.Length];
                enc.Encrypt(key, nonce, null, payload, result);
                
                result[0..payload.Length].CopyTo(target);
                result[payload.Length..].CopyTo(tag);
            }
            else if (op == 1)
            {
                var encoded = BWT.Encode(payload.ToArray())[0..payload.Length];
                encoded.CopyTo(target);
            }

            cIX = nIX;
            nIX = BitConverter.ToInt32(target[0..4]) % (sbox.Length - expSz);
        }

        return SHA256.HashData(sbox);
    }

    public unsafe static SHA256Hash Hash(Concat concat)
    {
        var scratchpad = new byte[MAX_MEM];
        return Hash(concat, scratchpad);
    }
}
