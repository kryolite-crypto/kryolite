using System.Diagnostics.Contracts;
using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class BlockchainRepository : IBlockchainRepository
{
    private readonly string DATA_PATH = "data/blockchain.dat";

    public BlockchainRepository()
    {

    }

    public long Count()
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<Block>().LongCount();
    }

    public void Add(Block block, ChainState chainState)
    {
        Contract.Equals(0, chainState.Id);

        using var db = new LiteDatabase(DATA_PATH);
        db.BeginTrans();
        db.GetCollection<Block>().Insert(block);
        db.GetCollection<ChainState>().Upsert(chainState);
        db.Commit();
    }

    public Block GetBlock(long id)
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<Block>().FindById(id); // TODO: id might not work
    }

    public ChainState GetChainState()
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<ChainState>().FindById(0) ?? new ChainState();
    }

    public List<Block> Tail(int count)
    {
        using var db = new LiteDatabase(DATA_PATH);
        var blocks = db.GetCollection<Block>().LongCount();
        var startId = blocks - count;

        var results = db.GetCollection<Block>()
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .Limit(count)
            .ToList();

        results.Reverse();

        return results;
    }

    public Block Last()
    {
        using var db = new LiteDatabase(DATA_PATH);

        return db.GetCollection<Block>()
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .FirstOrDefault();
    }
}
