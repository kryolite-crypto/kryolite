using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Redbus.Events;
using Wasmtime;

namespace Kryolite.Node.Executor;

public class ExecutorContext : IExecutorContext
{
    public IBlockchainRepository Repository { get; }
    private Dictionary<Address, Ledger> Wallets { get; } = new();
    private Dictionary<Address, Contract> Contracts { get; } = new();
    private Dictionary<SHA256Hash, Token> Tokens { get; } = new();
    private List<EventBase> Events { get; } = new();
    private Random Rand { get; set; } = Random.Shared;

    public ExecutorContext(IBlockchainRepository repository)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Random GetRand()
    {
        return Rand;
    }

    public void SetRand(long seed)
    {
        Rand = new Random((int)(seed % int.MaxValue));
    }

    public Contract? GetContract(Address? address)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        if (!Contracts.TryGetValue(address, out var contract))
        {
            contract = Repository.GetContract(address);

            if (contract is null)
            {
                return null;
            }

            Contracts.Add(address, contract);
        }

        return contract;
    }

    public void AddContract(Contract contract)
    {
        Contracts.Add(contract.Address, contract);
    }

    public Ledger? GetWallet(Address? address)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        if (!Wallets.TryGetValue(address, out var wallet))
        {
            wallet = Repository.GetWallet(address);

            if (wallet is null)
            {
                return null;
            }

            Wallets.Add(address, wallet);
        }

        return wallet;
    }

    public Ledger GetOrNewWallet(Address? address)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        if (!Wallets.TryGetValue(address, out var wallet))
        {
            wallet = Repository.GetWallet(address);

            if (wallet is null)
            {
                wallet = new Ledger(address);
            }

            Wallets.Add(address, wallet);
        }

        return wallet;
    }

    public Token? GetToken(SHA256Hash tokenId)
    {
        if (!Tokens.TryGetValue(tokenId, out var token))
        {
            token = Repository.GetToken(tokenId);

            if (token is null)
            {
                return null;
            }

            Tokens.Add(tokenId, token);
        }

        return token;
    }

    public void AddToken(Token token)
    {
        Tokens.Add(token.TokenId, token);
    }

    public List<EventBase> GetEvents()
    {
        return Events;
    }

    public void AddEvents(List<EventBase> events)
    {
        Events.AddRange(events);
    }

    public IBlockchainRepository GetRepository()
    {
        return Repository;
    }

    public void Save()
    {
        foreach (var contract in Contracts)
        {
            if (contract.Value.CurrentSnapshot is null)
            {
                continue;
            }

            contract.Value.Snapshots.Add(contract.Value.CurrentSnapshot);
        }

        Repository.UpdateWallets(Wallets.Values);
        Repository.UpdateContracts(Contracts.Values);
        Repository.UpdateTokens(Tokens.Values);
    }
}
