using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Redbus.Events;
using RocksDbSharp;
using Wasmtime;

namespace Kryolite.Node.Executor;

public class ExecutorContext : IExecutorContext
{
    public IStoreRepository Repository { get; }
    private Dictionary<Address, Ledger> Wallets { get; }
    private long TotalStake { get; }
    private long Height { get; }
    private Dictionary<Address, Contract> Contracts { get; } = new();
    private Dictionary<(Address contract, SHA256Hash tokenId), Token> Tokens { get; } = new();
    private List<EventBase> Events { get; } = new();
    private Random Rand { get; set; } = Random.Shared;

    public ExecutorContext(IStoreRepository repository, Dictionary<Address, Ledger> wallets, long totalStake, long height)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Wallets = wallets ?? throw new ArgumentNullException(nameof(wallets));
        TotalStake = totalStake;
        Height = height;
    }

    public Random GetRand()
    {
        return Rand;
    }

    public long GetTotalStake()
    {
        return TotalStake;
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

    public Token? GetToken(Address contract, SHA256Hash tokenId)
    {
        if (!Tokens.TryGetValue((contract, tokenId), out var token))
        {
            token = Repository.GetToken(contract, tokenId);

            if (token is null)
            {
                return null;
            }

            Tokens.Add((contract, tokenId), token);
        }

        return token;
    }

    public void AddToken(Token token)
    {
        Tokens.Add((token.Contract, token.TokenId), token);
    }

    public List<EventBase> GetEvents()
    {
        return Events;
    }

    public void AddEvents(List<EventBase> events)
    {
        Events.AddRange(events);
    }

    public IStoreRepository GetRepository()
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

            Repository.AddContractSnapshot(contract.Value.Address, Height, contract.Value.CurrentSnapshot);
        }

        Repository.UpdateWallets(Wallets.Values);
        Repository.UpdateContracts(Contracts.Values);
        Repository.UpdateTokens(Tokens.Values);
    }

    public long GetHeight()
    {
        return Height;
    }
}
