using CSChaCha20;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kryolite.Shared;

public static class KryoHash2
{
    public static readonly int MEM_MULTIPLIER = 8;
    public static readonly int EXP_MULTIPLIER = 1024;
    public static readonly int ROUND_MULTIPLIER = 8;
    public static readonly int MAX_MEM = MEM_MULTIPLIER * 1024 * 1024;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe static SHA256Hash Hash(Concat concat, Span<byte> scratchpad)
    {
        try
        {
            scratchpad.Clear();

            var rules = SHA256.HashData(concat.Buffer[0..32]);

            int expSz = Math.Max(rules[0] % EXP_MULTIPLIER, 32);
            int sboxSz = Math.Max(rules[1] % MEM_MULTIPLIER, (byte)1) * 1024 * 1024;
            int rounds = Math.Max(rules[2] % ROUND_MULTIPLIER, (byte)1) * 1024;

            var sbox = scratchpad.Slice(0, sboxSz);

            var key = SHA256.HashData(concat.Buffer);
            var nonce = SHA256.HashData(key)[0..12];

            using var enc = new ChaCha20(key, nonce, 0);

            var cIX = 0;
            var nIX = BitConverter.ToInt32(nonce[0..4]) % (sbox.Length - expSz);

            var payload = new byte[expSz];

            enc.EncryptBytes(payload, payload);

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
                    var result = new byte[target.Length];
                    enc.EncryptBytes(payload, result);
                    result.CopyTo(target);
                }
                else if (op == 1)
                {
                    var encoded = BWT.Encode(payload)[0..payload.Length];
                    encoded.CopyTo(target);
                }

                cIX = nIX;
                nIX = BitConverter.ToInt32(target[0..4]) % (sbox.Length - expSz);
            }

            return SHA256.HashData(sbox);
        } 
        catch (Exception ex)
        {
            throw new Exception($"hash failure, nonce [{string.Join("-", concat.Buffer)}]", ex);
        }
    }

    public unsafe static SHA256Hash Hash(Concat concat)
    {
        var mem = Marshal.AllocHGlobal(MAX_MEM);
        var scratchpad = new Span<byte>(mem.ToPointer(), MAX_MEM);
        var hash = Hash(concat, scratchpad);
        Marshal.FreeHGlobal(mem);
        return hash;
    }
}
