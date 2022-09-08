using LiteDB;
using Tenray.ZoneTree;
using Tenray.ZoneTree.Comparers;
using Tenray.ZoneTree.Serializers;

namespace Marccacoin;

public class BlockchainRepository : IBlockchainRepository
{
    private readonly string DATA_PATH = "data/data.db";

    public BlockchainRepository()
    {
        using var db = new LiteDatabase(DATA_PATH);
        db.GetCollection<Block>()
            .EnsureIndex<long>(x => x.Header.Id);
    }

    public long Count()
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<Block>().LongCount();
    }

    public void Add(Block block)
    {
        using var db = new LiteDatabase(DATA_PATH);
        db.GetCollection<Block>().Insert(block.Header.Id, block);
    }

    public Block Get(long id)
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<Block>().FindById(id);
    }

    public List<Block> Tail(int count)
    {
        using var db = new LiteDatabase(DATA_PATH);
        var blocks = db.GetCollection<Block>().LongCount();
        var startId = blocks - count;

        var results = db.GetCollection<Block>()
            .Query()
            .OrderByDescending<long>(x => x.Header.Id)
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
            .OrderByDescending<long>(x => x.Header.Id)
            .FirstOrDefault();
    }
}
