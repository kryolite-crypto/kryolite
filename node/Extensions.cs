using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Wasmtime;

namespace Kryolite;

public static class Extensions
{
    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        if (task == await Task.WhenAny(task, Task.Delay(timeout)))
        {
            return await task;
        }

        throw new TimeoutException();
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

    /// <summary>
    /// Returns true if the IP address is in a private range.<br/>
    /// IPv4: Loopback, link local ("169.254.x.x"), class A ("10.x.x.x"), class B ("172.16.x.x" to "172.31.x.x") and class C ("192.168.x.x").<br/>
    /// IPv6: Loopback, link local, site local, unique local and private IPv4 mapped to IPv6.<br/>
    /// </summary>
    /// <param name="ip">The IP address.</param>
    /// <returns>True if the IP address was in a private range.</returns>
    /// <example><code>bool isPrivate = IPAddress.Parse("127.0.0.1").IsPrivate();</code></example>
    public static bool IsPrivate(this IPAddress ip)
    {
        // Map back to IPv4 if mapped to IPv6, for example "::ffff:1.2.3.4" to "1.2.3.4".
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        // Checks loopback ranges for both IPv4 and IPv6.
        if (IPAddress.IsLoopback(ip)) return true;

        // IPv4
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return IsPrivateIPv4(ip.GetAddressBytes());

        // IPv6
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
        }

        throw new NotSupportedException($"IP address family {ip.AddressFamily} is not supported, expected only IPv4 (InterNetwork) or IPv6 (InterNetworkV6).");
    }

    public static bool IsPublic(this IPAddress ip)
    {
        return !ip.IsPrivate();
    }

    private static bool IsPrivateIPv4(byte[] ipv4Bytes)
    {
        // Link local (no IP assigned by DHCP): 169.254.0.0 to 169.254.255.255 (169.254.0.0/16)
        bool IsLinkLocal() => ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254;

        // Class A private range: 10.0.0.0 – 10.255.255.255 (10.0.0.0/8)
        bool IsClassA() => ipv4Bytes[0] == 10;

        // Class B private range: 172.16.0.0 – 172.31.255.255 (172.16.0.0/12)
        bool IsClassB() => ipv4Bytes[0] == 172 && ipv4Bytes[1] >= 16 && ipv4Bytes[1] <= 31;

        // Class C private range: 192.168.0.0 – 192.168.255.255 (192.168.0.0/16)
        bool IsClassC() => ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168;

        return IsLinkLocal() || IsClassA() || IsClassB() || IsClassC();
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
            validator = repository.GetStake(address);

            if (validator is null)
            {
                return false;
            }

            validators.Add(address, validator);
        }

        return true;
    }
}
