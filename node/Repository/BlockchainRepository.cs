using System.Diagnostics.Contracts;
using System.Numerics;
using LiteDB;
using Marccacoin.Shared;

namespace Marccacoin;

public class BlockchainRepository : IBlockchainRepository
{
    private readonly string DATA_PATH = "data/blocks.dat";

    public BlockchainRepository()
    {
        BsonMapper.Global.RegisterType<Difficulty>
        (
            serialize: (diff) => BitConverter.GetBytes(diff.Value),
            deserialize: (bson) => new Difficulty { Value = BitConverter.ToUInt32(bson.AsBinary) }
        );

        BsonMapper.Global.RegisterType<SHA256Hash>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Nonce>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Signature>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Address>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Shared.PublicKey>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<Shared.PrivateKey>
        (
            serialize: (hash) => hash.Buffer,
            deserialize: (bson) => bson.AsBinary
        );

        BsonMapper.Global.RegisterType<BigInteger>
        (
            serialize: (bigint) => bigint.ToByteArray(),
            deserialize: (bson) => new BigInteger(bson.AsBinary, true)
        );

        BsonMapper.Global.Entity<Block>()
            .DbRef(x => x.Header, typeof(BlockHeader).Name)
            .DbRef(x => x.Transactions, typeof(Transaction).Name);

        Directory.CreateDirectory("data");
    }

    public long Count()
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<Block>().LongCount();
    }

    public void Add(Block block, ChainState chainState)
    {
        Contract.Equals(0, chainState.Id);

        block.Header.Id = block.Id;

        using var db = new LiteDatabase(DATA_PATH);

        try {
            db.BeginTrans();
            db.GetCollection<BlockHeader>().Insert(block.Header);
            db.GetCollection<Transaction>().Insert(block.Transactions);
            db.GetCollection<Block>().Insert(block);
            db.GetCollection<ChainState>().Upsert(chainState);
            db.Commit();
        } catch (Exception ex) {
            db.Rollback();
            throw new Exception("AddBlock failed", ex);
        }
    }

    public Block GetBlock(long id)
    {
        using var db = new LiteDatabase(DATA_PATH);
        return db.GetCollection<Block>()
            .Include(x => x.Header)
            .IncludeCollection(x => x.Transactions)
            .FindById(id);
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
        using var db = new LiteDatabase(DATA_PATH);

        return db.GetCollection<Block>()
            .Include(x => x.Header)
            .IncludeCollection(x => x.Transactions)
            .Query()
            .OrderByDescending<long>(x => x.Id)
            .FirstOrDefault();
    }

    public ulong GetBalance(Address address)
    {
        using var db = new LiteDatabase(DATA_PATH);

        return (ulong)db.GetCollection<Transaction>()
            .Query()
            .Where("$.To = @0", new BsonValue(address))
            .Select(x => (long)x.Value)
            .ToList()
            .Sum();
    }

    public List<Transaction> GetTransactions(Address address, int count)
    {
        using var db = new LiteDatabase(DATA_PATH);

        return db.GetCollection<Transaction>()
            .Query()
            .Where("$.To = @0", new BsonValue(address))
            .OrderByDescending(x => x.Id)
            .Limit(count)
            .ToList();
    }
}
