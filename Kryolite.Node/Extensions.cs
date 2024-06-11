using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Microsoft.Extensions.Configuration;
using Wasmtime;

namespace Kryolite;

public static class Extensions
{
    public static async Task<bool> WithTimeout(this Task task, TimeSpan timeout, CancellationToken token)
    {
        if (task == await Task.WhenAny(task, Task.Delay(timeout, token)))
        {
            await task;
            return true;
        }

        return false;
    }

    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        if (task == await Task.WhenAny(task, Task.Delay(timeout)))
        {
            return await task;
        }

        throw new TimeoutException();
    }

    public static Task WhenCancelled(this CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        cancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).SetResult(true), tcs);
        return tcs.Task;
    }

    public static Address ReadAddress(this Memory memory, int address)
    {
        return (Address)memory.GetSpan(address, Address.ADDRESS_SZ);
    }

    public static SHA256Hash ReadU256(this Memory memory, int address)
    {
        return (SHA256Hash)memory.GetSpan(address, SHA256Hash.HASH_SZ);
    }

    public static void WriteBuffer(this Memory memory, int address, byte[] buffer)
    {
        foreach (var b in buffer) 
        {
            memory.WriteByte(address, b);
            address++;
        }
    }

    public static string ToHostname(this Uri uri)
    {
        return uri.ToString().TrimEnd('/');
    }

    public static bool TryGetWallet(this WalletCache ledger, Address address, IStoreRepository repository, [NotNullWhen(true)] out Ledger? wallet)
    {
        wallet = null;

        if (address == Address.NULL_ADDRESS)
        {
            return false;
        }

        if (!ledger.TryGetValue(address, out wallet))
        {
            wallet = repository.GetWallet(address);

            if (wallet is null)
            {
                return false;
            }

            ledger.Add(address, wallet);
        }

        return true;
    }

   public static bool TryGetContract(this Dictionary<Address, Contract> ledger, Address address, IStoreRepository repository, [NotNullWhen(true)] out Contract? contract)
    {
        contract = null;

        if (address == Address.NULL_ADDRESS)
        {
            return false;
        }

        if (!ledger.TryGetValue(address, out contract))
        {
            contract = repository.GetContract(address);

            if (contract is null)
            {
                return false;
            }

            ledger.Add(address, contract);
        }

        return true;
    }

    public static bool TryGetToken(this Dictionary<(Address, SHA256Hash), Token> tokens, Address contract, SHA256Hash tokenId, IStoreRepository repository, [NotNullWhen(true)] out Token? token)
    {
        token = null;

        if (tokenId == SHA256Hash.NULL_HASH)
        {
            return false;
        }

        if (!tokens.TryGetValue((contract, tokenId), out token))
        {
            token = repository.GetToken(contract, tokenId);

            if (token is null)
            {
                return false;
            }

            tokens.Add((contract, tokenId), token);
        }

        return true;
    }

    public static bool TryGetValidator(this ValidatorCache validators, Address address, IStoreRepository repository, [NotNullWhen(true)] out Validator? validator)
    {
        validator = null;

        if (address == Address.NULL_ADDRESS)
        {
            return false;
        }

        if (!validators.TryGetValue(address, out validator))
        {
            validator = repository.GetValidator(address);

            if (validator is null)
            {
                return false;
            }

            validators.Add(address, validator);
        }

        return true;
    }

    public static string GetDataDir(this IConfiguration config)
    {
        var defaultDataDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kryolite");
        return config.GetValue("data-dir", defaultDataDir) ?? defaultDataDir;
    }
}
