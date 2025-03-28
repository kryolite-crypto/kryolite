using System.Diagnostics.CodeAnalysis;
using Kryolite.EventBus;
using Kryolite.Node.Procedure;
using Kryolite.Node.Repository;
using Kryolite.Model;
using Kryolite.Type;
using Kryolite.Interface;
using Kryolite.Shared;

namespace Kryolite.Node.Executor;

public interface IExecutorContext
{
    Random GetRand();
    void SetRand(long seed);

    View GetLastView();
    public ulong GetTotalStake();
    public long GetHeight();

    Contract? GetContract(Address? address);
    void AddContract(Contract contract);

    Ledger? GetWallet(Address? address);
    Ledger GetOrNewWallet(Address? address);

    Token? GetToken(Address contract, SHA256Hash tokenId);
    void AddToken(Token token);

    List<IEvent> GetEvents();
    void AddEvents(List<IEvent> events);

    IStoreRepository GetRepository();
    void Save();

    void AddEvent(IEvent ev);
    bool TryGetValidator(Address address, [NotNullWhen(true)]out Validator? validator);
    void AddValidator(Validator validator);

    Dictionary<Address, Validator> Validators { get; }
    Dictionary<Address, Ledger> Ledger { get; }
}
