using System.Diagnostics.Contracts;
using System.Numerics;
using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class BlockRepository : TransactionalRepository
{
    public BlockRepository(bool transactional = false) : base("data/blocks.dat", transactional)
    {
        BsonMapper.Global.Entity<Block>()
            .DbRef(x => x.Header, typeof(BlockHeader).Name)
            .DbRef(x => x.Transactions, typeof(Transaction).Name);

        Database.GetCollection<Transaction>()
            .EnsureIndex(x => x.To, false);
    }

    public long Count()
    {
        return Database.GetCollection<Block>()
            .LongCount();
    }

    public void Add(Block block, ChainState chainState)
    {
        Contract.Equals(0, chainState.Id);
        Contract.Equals(true, Transactional);

        block.Header.Id = block.Id;

        Database.GetCollection<BlockHeader>().Insert(block.Header);
        Database.GetCollection<Transaction>().Insert(block.Transactions);
        Database.GetCollection<Block>().Insert(block);
        Database.GetCollection<ChainState>().Upsert(chainState);
    }

    public Block GetBlock(long id)
    {
        return Database.GetCollection<Block>()
            .Include(x => x.Header)
            .IncludeCollection(x => x.Transactions)
            .FindById(id);
    }

    public ChainState GetChainState()
    {
        return Database.GetCollection<ChainState>().FindById(0) ?? new ChainState();
    }

    public List<Block> Tail(int count)
    {
        var blocks = Database.GetCollection<Block>().LongCount();
        var startId = blocks - count;

        var results = Database.GetCollection<Block>()
            .Include(x => x.Header)
            .IncludeCollection(x => x.Transactions)
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .Limit(count)
            .ToList();

        results.Reverse();

        return results;
    }

    public Block Last()
    {
        return Database.GetCollection<Block>()
            .Include(x => x.Header)
            .IncludeCollection(x => x.Transactions)
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .FirstOrDefault();
    }
}
