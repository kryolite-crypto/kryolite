using Kryolite.Shared.Algorithm;
using Kryolite.Shared.Blockchain;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Wasmtime;

namespace Kryolite.Shared;

public static class Grasshopper
{
    public const int ROUNDS = 80;
    public const int STATE_SZ = 16;
    public const int OPS_COUNT = STATE_SZ * sizeof(long);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe static SHA256Hash Hash(Concat concat)
    {
        try
        {
            var vm = new GrasshopperStackMachine();
            var state = stackalloc long[STATE_SZ];
            var ops = new Span<byte>(state, OPS_COUNT);

            SHA512.TryHashData(concat.Buffer, ops, out var _);
            SHA512.TryHashData(ops.Slice(0, 64), ops.Slice(64), out var _);

            for (var i = 0; i < STATE_SZ * ROUNDS; i++)
            {
                vm.Reset();

                for (var x = 0; x < STATE_SZ; x++)
                {
                    vm.Emit(Op.LdInt64, state[x]);
                    vm.Emit(Op.StLoc, x);
                }

                vm.Emit(Op.LdLoc, i % STATE_SZ);

                for (var x = 0; x < OPS_COUNT; x++)
                {
                    var op = ops[x] % 110;
                    var loc = op % STATE_SZ;

                    if (op < 16) // 14,5% chance
                    {
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Add);
                    }
                    else if (op < 32) // 14,5% chance
                    {
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Mul);
                    }
                    else if (op < 48) // 14,5% chance
                    {
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Or);
                    }
                    else if (op < 64) // 14,5% chance
                    {
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.Xor);
                    }
                    else if (op < 80) // 14,5% chance
                    {
                        vm.Emit(Op.LdInt32, loc);
                        vm.Emit(Op.Rotr);
                    }
                    else if (op < 96) // 14,5% chance
                    {
                        vm.Emit(Op.LdInt32, loc);
                        vm.Emit(Op.Rotl);
                    }
                    else if (op < 98) // 1,8% chance
                    {
                        // SHA512 - Ch
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.And);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.Not);
                        vm.Emit(Op.LdInt32, (loc + 1) % STATE_SZ);
                        vm.Emit(Op.And);
                        vm.Emit(Op.Xor);
                    }
                    else if (op < 100) // 1,8% chance
                    {
                        // SHA512 - Maj
                        vm.Emit(Op.StLoc, 16);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.And);
                        vm.Emit(Op.LdLoc, 16);
                        vm.Emit(Op.LdLoc, (loc + 1) % STATE_SZ);
                        vm.Emit(Op.And);
                        vm.Emit(Op.Xor);
                        vm.Emit(Op.LdLoc, loc);
                        vm.Emit(Op.LdLoc, (loc + 1) % STATE_SZ);
                        vm.Emit(Op.And);
                        vm.Emit(Op.Xor);
                    }
                    else if (op < 102) // 1,8% chance
                    {
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
                    }
                    else if (op < 104) // 1,8% chance
                    {
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
                    }
                    else if (op < 106) // 1,8% chance
                    {
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
                    }
                    else if (op < 108) // 1,8% chance
                    {
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
                    }
                    else // 1,8% chance
                    {
                        // Shuffle incoming
                        SHA512.TryHashData(ops.Slice(Math.Min(x + 1, 64), 64), ops.Slice(x), out var _);
                    }
                }

                vm.Emit(Op.Ret);

                state[i % STATE_SZ] += vm.Checksum;
                state[i % STATE_SZ] += vm.Invoke();
                state[i % STATE_SZ] += vm.Checksum;
            }

            return SHA256.HashData(ops);
        }
        catch (Exception ex)
        {
            throw new Exception($"hash failure, nonce [{string.Join("-", concat.Buffer)}]", ex);
        }
    }
}
