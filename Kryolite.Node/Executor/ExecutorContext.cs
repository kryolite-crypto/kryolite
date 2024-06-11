using System.Diagnostics.CodeAnalysis;
using Kryolite.EventBus;
using Kryolite.Node.Procedure;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node.Executor;

public class ExecutorContext : IExecutorContext
{
    public IStoreRepository Repository { get; }
    public View View { get; }
    private ulong TotalStake { get; }
    private long Height { get; }
    private Dictionary<Address, Contract> Contracts { get; } = new();
    private Dictionary<(Address contract, SHA256Hash tokenId), Token> Tokens { get; } = new();
    private List<EventBase> Events { get; } = new();
    private Random Rand { get; set; } = Random.Shared;

    public ValidatorCache Validators { get; private set; }
    public WalletCache Ledger { get; private set; }

    public ExecutorContext(IStoreRepository repository, WalletCache wallets, ValidatorCache validators, View view, ulong totalStake, long height)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Ledger = wallets ?? throw new ArgumentNullException(nameof(wallets));
        Validators = validators ?? throw new ArgumentNullException(nameof(validators));
        View = view ?? throw new ArgumentNullException(nameof(view));
        TotalStake = totalStake;
        Height = height;
    }

    public Random GetRand()
    {
        return Rand;
    }

    public ulong GetTotalStake()
    {
        return TotalStake;
    }

    public View GetLastView()
    {
        return View;
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

        if (!Ledger.TryGetValue(address, out var wallet))
        {
            wallet = Repository.GetWallet(address);

            if (wallet is null)
            {
                return null;
            }

            Ledger.Add(address, wallet);
        }

        return wallet;
    }

    public Ledger GetOrNewWallet(Address? address)
    {
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        if (!Ledger.TryGetValue(address, out var wallet))
        {
            wallet = Repository.GetWallet(address);

            if (wallet is null)
            {
                wallet = new Ledger(address);
            }

            Ledger.Add(address, wallet);
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
        Tokens.TryAdd((token.Contract, token.TokenId), token);
    }

    public List<EventBase> GetEvents()
    {
        return Events;
    }

    public void AddEvent(EventBase ev)
    {
        Events.Add(ev);
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
        foreach (var ledger in Ledger.Values)
        {
            if (ledger.Changed)
            {
                Repository.UpdateWallet(Height, ledger);
                ledger.Changed = false;
            }
        }

        foreach (var validator in Validators.Values)
        {
            if (validator.Changed)
            {
                Repository.SetValidator(Height, validator);
                validator.Changed = false;
            }
        }

        foreach (var contract in Contracts)
        {
            if (contract.Value.CurrentSnapshot is null)
            {
                continue;
            }

            Repository.AddContractSnapshot(contract.Value.Address, Height, contract.Value.CurrentSnapshot);
        }

        foreach (var token in Tokens.Values)
        {
            Repository.SetToken(token, Height);
        }
    }

    public long GetHeight()
    {
        return Height;
    }

    public bool TryGetValidator(Address address, [NotNullWhen(true)] out Validator? validator)
    {
        if (!Validators.ContainsKey(address))
        {
            var stake = Repository.GetValidator(address);

            if (stake is null)
            {
                validator = null;
                return false;
            }

            Validators.Add(address, stake);
        }

        validator = Validators[address];
        return true;
    }

    public void AddValidator(Validator validator)
    {
        Validators.Add(validator.NodeAddress, validator);
    }
}
