using Kryolite.EventBus;
using Kryolite.Node.Repository;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    List<EventBase> GetEvents();
    void AddEvents(List<EventBase> events);

    IStoreRepository GetRepository();
    void Save();

    void AddEvent(EventBase ev);
}
