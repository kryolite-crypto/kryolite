using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Kryolite.Node;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using QuikGraph;
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

    public static AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> AsGraph(this List<TransactionDto> transactions)
    {
        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>();

        graph.AddVertexRange(transactions.Select(x => x.CalculateHash()));

        foreach (var tx in transactions)
        {
            foreach (var parent in tx.Parents)
            {
                if (graph.ContainsVertex(parent))
                {
                    graph.AddEdge(new Edge<SHA256Hash>(tx.CalculateHash(), parent));
                }
            }
        }

        return graph;
    }

    public static AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> AsGraph(this List<Transaction> transactions)
    {
        var graph = new AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>>();

        graph.AddVertexRange(transactions.Select(x => x.CalculateHash()));

        foreach (var tx in transactions)
        {
            foreach (var parent in tx.Parents)
            {
                if (graph.ContainsVertex(parent))
                {
                    graph.AddEdge(new Edge<SHA256Hash>(tx.CalculateHash(), parent));
                }
            }
        }

        return graph;
    }

    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this T[] array)
    {
        return array;
    }

    public static Task<bool> WaitOneAsync(this ManualResetEvent manualResetEvent)
    {
        return Task.Run(() => manualResetEvent.WaitOne());
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
            return ip.IsIPv6LinkLocal ||
#if NET6_0
                    ip.IsIPv6UniqueLocal ||
#endif
                    ip.IsIPv6SiteLocal;
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

    public static Ledger? TryGetWallet(this Dictionary<Address, Ledger> ledger, Address address, IStoreRepository repository)
    {
        if (address == Address.NULL_ADDRESS)
        {
            return null;
        }

        if (!ledger.TryGetValue(address, out var wallet))
        {
            wallet = repository.GetWallet(address);

            if (wallet is not null)
            {
                ledger.Add(address, wallet);
            }
        }

        return wallet;
    }

   public static Contract? TryGetContract(this Dictionary<Address, Contract> ledger, Address address, IStoreRepository repository)
    {
        if (address == Address.NULL_ADDRESS)
        {
            return null;
        }

        if (!ledger.TryGetValue(address, out var contract))
        {
            contract = repository.GetContract(address);

            if (contract is not null)
            {
                ledger.Add(address, contract);
            }
        }

        return contract;
    }

   public static Token? TryGetToken(this Dictionary<SHA256Hash, Token> tokens, Address contract, SHA256Hash tokenId, IStoreRepository repository)
    {
        if (tokenId == SHA256Hash.NULL_HASH)
        {
            return null;
        }

        if (!tokens.TryGetValue(tokenId, out var token))
        {
            token = repository.GetToken(contract, tokenId);

            if (token is not null)
            {
                tokens.Add(tokenId, token);
            }
        }

        return token;
    }
}
