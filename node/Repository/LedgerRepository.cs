using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class LedgerRepository : TransactionalRepository
{
    public LedgerRepository(bool transactional = false) : base("Filename=data/ledger.dat;Connection=shared", transactional)
    {
        Database.GetCollection<LedgerWallet>()
            .EnsureIndex(x => x.Address, true);
    }

    public LedgerWallet GetWallet(Address address)
    {
        return Database.GetCollection<LedgerWallet>()
            .Query()
            .Where("$.Address = @0", new BsonValue(address))
            .FirstOrDefault();
    }

    public void UpdateWallet(LedgerWallet wallet)
    {
        Database.GetCollection<LedgerWallet>()
            .Upsert(wallet);
    }

    public void UpdateWallets(IEnumerable<LedgerWallet> wallets)
    {
        Database.GetCollection<LedgerWallet>()
            .Upsert(wallets);
    }
}
