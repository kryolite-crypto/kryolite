using Kryolite.Shared;
using Wasmtime;

namespace Kryolite.Node.Executor;

public class ExecutorContext : IExecutorContext
{
    public BlockchainRepository Repository { get; }
    private Dictionary<Address, LedgerWallet> Wallets { get; } = new();
    private Dictionary<Address, Contract> Contracts { get; } = new();
    private Dictionary<SHA256Hash, Token> Tokens { get; } = new();
    private List<EventArgs> Events { get; } = new();
    private Random Rand { get; set; } = Random.Shared;

    public ExecutorContext(BlockchainRepository repository)
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

    public LedgerWallet? GetWallet(Address? address)
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

    public LedgerWallet GetOrNewWallet(Address? address)
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
                wallet = new LedgerWallet(address);
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

    public List<EventArgs> GetEvents()
    {
        return Events;
    }

    public void AddEvents(List<EventArgs> events)
    {
        Events.AddRange(events);
    }

    public BlockchainRepository GetRepository()
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
