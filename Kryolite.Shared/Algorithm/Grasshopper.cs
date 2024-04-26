using Kryolite.Shared.Blockchain;
using System.Security.Cryptography;

namespace Kryolite.Shared.Algorithm;

public static class Grasshopper
{
    public const int ROUNDS = 40;
    public const int STATE_SZ = 16;
    public const int OPS_COUNT_PER_ROUND = STATE_SZ * sizeof(long);
    public const int OPS_COUNT = OPS_COUNT_PER_ROUND * ROUNDS;

    public static SHA256Hash Hash(Concat concat)
    {
        Span<byte> buf = stackalloc byte[32];
        Hash(concat, buf);

        return buf;
    }

    public static unsafe void Hash(ReadOnlySpan<byte> concat, Span<byte> hash)
    {
        using var vm = new GrasshopperStackMachine();

        var state = stackalloc long[STATE_SZ * ROUNDS];
        var stateStart = (byte*)state;
        var stateEnd = stateStart + OPS_COUNT_PER_ROUND;

        var ops = new Span<byte>(state, OPS_COUNT);
        SHA512.TryHashData(concat, ops, out var _);

        for (var i = 0; i < STATE_SZ * ROUNDS; i++)
        {
            vm.Reset();

            SHA512.TryHashData(ops.Slice(i, 64), ops.Slice(i + 64), out _);

            for (var x = 0; x < STATE_SZ; x++)
            {
                vm.Emit(Op.LdInt64, state[x]);
                vm.Emit(Op.StLoc, x);
            }

            vm.Emit(Op.LdLoc, i % STATE_SZ);

            while (stateStart != stateEnd)
            {
                var op = *stateStart++ % 20;
                var loc = op % STATE_SZ;

                switch (op)
                {
                    case 0:
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Add);
                        break;
                    case 1:
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Mul);
                        break;
                    case 2:
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Or);
                        break;
                    case 3:
                        // Perform Xor only if values are not equal
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Cmp);
                        vm.Emit(Op.JmpIfZero, 4); // skip xor and restore stack value if compare returns zero
                        // if
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.Jmp, 1);
                        // else
                        vm.Emit(Op.LdLoc, 16);
                        break;
                    case 4:
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Xor);
                        break;
                    case 5:
                        vm.Emit(Op.LdInt32, loc);
                        vm.Emit(Op.Rotr);
                        break;
                    case 6:
                        vm.Emit(Op.LdInt32, loc);
                        vm.Emit(Op.Rotl);
                        break;
                    case 7:
                        // SHA512 - Ch
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.And);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.Not);
                        vm.Emit(Op.LdInt32, *stateStart % STATE_SZ);
                        vm.Emit(Op.And);
                        vm.Emit(Op.Xor);
                        break;
                    case 8:
                        // SHA512 - Maj
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.And);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, *stateStart % STATE_SZ);
                        vm.Emit(Op.And);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.LdLoc, *stateStart % STATE_SZ);
                        vm.Emit(Op.And);
                        vm.Emit(Op.Xor);
                        break;
                    case 9:
                        // SHA512 - Ro0
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 1);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 8);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 7);
                        vm.Emit(Op.ShrUn);
                        vm.Emit(Op.Xor);
                        break;
                    case 10:
                        // SHA512 - Ro1
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 19);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 61);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 6);
                        vm.Emit(Op.ShrUn);
                        vm.Emit(Op.Xor);
                        break;
                    case 11:
                        // SHA512 - Sig0
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 28);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 34);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 39);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.Xor);
                        break;
                    case 12:
                        // SHA 512 - Sig1
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 14);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 18);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt32, 41);
                        vm.Emit(Op.Rotr);
                        vm.Emit(Op.Xor);
                        break;
                    case 13:
                        vm.Emit(Op.LdInt32, loc % 96);
                        vm.Emit(Op.SHA256);
                        break;
                    case 14:
                        vm.Emit(Op.PopCnt);
                        break;
                    case 15:
                        vm.Emit(Op.LZCnt);
                        break;
                    case 16:
                        vm.Emit(Op.TZCnt);
                        break;
                    case 17:
                        // Perform CRC32C only if stack is not zero
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdInt64, 0);
                        vm.Emit(Op.Cmp);
                        vm.Emit(Op.JmpIfZero, 3); // skip crc and restore stack value if compare is zero
                        // if
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.CRC32C);
                        vm.Emit(Op.Jmp, 1);
                        // else
                        vm.Emit(Op.LdLoc, 16);
                        break;
                    case 18:
                        vm.Emit(Op.NoOp);
                        break;
                    case 19:
                        // Compare loc against stackptr value and add comparison result to stackptr value
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Cmp);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.Add);
                        break;
                }
            }

            stateStart -= OPS_COUNT_PER_ROUND;

            vm.Emit(Op.Ret);

            state[i % STATE_SZ] += vm.Checksum;
            state[i % STATE_SZ] += vm.Invoke();
            state[i % STATE_SZ] += vm.Checksum;
        }

        SHA256.TryHashData(ops, hash, out _);
    }
}
