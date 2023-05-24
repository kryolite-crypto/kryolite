using Kryolite.Shared;
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

    Contract? GetContract(Address? address);
    void AddContract(Contract contract);

    LedgerWallet? GetWallet(Address? address);
    LedgerWallet GetOrNewWallet(Address? address);

    Token? GetToken(SHA256Hash tokenId);
    void AddToken(Token token);

    List<EventArgs> GetEvents();
    void AddEvents(List<EventArgs> events);

    BlockchainRepository GetRepository();
    void Save();
}
