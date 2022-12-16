using System.Diagnostics.Contracts;
using System.Numerics;
using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class BlockRepository : TransactionalRepository
{
    public BlockRepository(bool transactional = false) : base("/blocks.dat;Connection=shared", transactional)
    {
        BsonMapper.Global.Entity<PowBlock>()
            .Id<long>(x => x.Id)
            .DbRef<List<Transaction>>(x => x.Transactions);

        BsonMapper.Global.Entity<PosBlock>()
            .Id<long>(x => x.Id)
            .DbRef<List<Transaction>>(x => x.Transactions)
            .DbRef<PowBlock>(x => x.Pow!)
            .DbRef<List<Vote>>(x => x.Votes);

        BsonMapper.Global.Entity<Vote>()
            .Id<long>(x => x.Id);

        Database.GetCollection<PowBlock>()
            .EnsureIndex(x => x.Height, false);

        Database.GetCollection<PosBlock>()
            .EnsureIndex(x => x.Height, false);

        Database.GetCollection<Vote>()
            .EnsureIndex(x => x.Height, false);

        Database.GetCollection<Transaction>()
            .EnsureIndex(x => x.To, false);

        Database.GetCollection<LedgerWallet>()
            .EnsureIndex(x => x.Address, true);
    }

    public long Count()
    {
        return Database.GetCollection<PowBlock>()
            .LongCount();
    }

    public void Add(PosBlock block, ChainState chainState)
    {
        Contract.Equals(0, chainState.Id);
        Contract.Equals(true, Transactional);

        if (block.Pow is not null)
        {
            Database.GetCollection<Transaction>().InsertBulk(block.Pow.Transactions);
            Database.GetCollection<PowBlock>().Insert(block.Pow);
        }

        // Fetch pending votes for block
        var pending = Database.GetCollection<Vote>()
            .Query()
            .Where(x => x.Height == block.Height)
            .Where("$.Hash = @0", new BsonValue(block.GetHash()))
            .ToEnumerable();
        
        Database.GetCollection<Transaction>().InsertBulk(block.Transactions);
        Database.GetCollection<Vote>().InsertBulk(block.Votes);

        block.Votes.AddRange(pending);

        Database.GetCollection<PosBlock>().Insert(block);
        Database.GetCollection<ChainState>().Upsert(chainState);
    }

    public void Add(List<PowBlock> blocks, ChainState chainState)
    {
        Contract.Equals(0, chainState.Id);
        Contract.Equals(true, Transactional);

        Database.GetCollection<PowBlock>().InsertBulk(blocks);
        Database.GetCollection<ChainState>().Upsert(chainState);
    }

    public void SaveState(ChainState chainState)
    {
        Database.GetCollection<ChainState>().Upsert(chainState);
    }

    public PowBlock? GetBlock(long height)
    {
        return Database.GetCollection<PowBlock>()
            .Find(x => x.Height == height)
            .FirstOrDefault(); // TODO: get the one with most votes?
    }

    public void Delete(long id)
    {
        Database.GetCollection<PowBlock>()
            .Delete(id);
    }

    public ChainState GetChainState()
    {
        return Database.GetCollection<ChainState>().FindById(0) ?? new ChainState();
    }

    public List<PowBlock> Tail(int count)
    {
        var blocks = Database.GetCollection<PowBlock>().LongCount();
        var startId = blocks - count;

        var results = Database.GetCollection<PowBlock>()
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .Limit(count)
            .ToList();

        results.Reverse();

        return results;
    }

    public List<PowBlock> Tail(long start, int count)
    {
        var blocks = Database.GetCollection<PowBlock>().LongCount();
        var startId = blocks - count;

        var results = Database.GetCollection<PowBlock>()
            .Query()
            .Where(x => x.Id < start)
            .OrderByDescending<long>(x => x.Id)
            .Limit(count)
            .ToList();

        results.Reverse();

        return results;
    }

    public PowBlock Last()
    {
        return Database.GetCollection<PowBlock>()
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .Include(x => x.Transactions)
            .FirstOrDefault();
    }

    public List<PowBlock> GetFrom(long id)
    {
        return Database.GetCollection<PowBlock>()
            .Query()
            .Where(x => x.Id > id)
            .ToList();
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

    public void AddVote(Vote vote)
    {
        Database.GetCollection<Vote>()
            .Insert(vote);
    }
}
