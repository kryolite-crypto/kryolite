using System.Diagnostics.Contracts;
using Kryolite.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kryolite.Node;

public class BlockchainRepository : IDisposable
{
    public BlockchainContext Context { get; }

    private static PooledDbContextFactory<BlockchainContext>? Factory { get; set; }

    public BlockchainRepository()
    {
        if (Factory is null)
        {
            var walletPath = Path.Join(BlockchainService.DATA_PATH, "blocks.dat");

            var options = new DbContextOptionsBuilder<BlockchainContext>()
                .UseSqlite($"Data Source={walletPath}")
                .Options;

            Factory = new PooledDbContextFactory<BlockchainContext>(options);
            Factory.CreateDbContext().Database.EnsureCreated();
        }

        Context = Factory.CreateDbContext();
    }

    public long Count()
    {
        return Context.PosBlocks
            .Select(x => x.Height)
            .DefaultIfEmpty()
            .Max();
    }

    public void Add(PosBlock block, ChainState chainState)
    {
        Contract.Equals(1, chainState.Id);

        var pendingVotes = Context.Votes
            .Where(x => x.Height == block.Height)
            .Where(x => Enumerable.SequenceEqual((byte[])x.Hash, (byte[])block.GetHash()))
            .ToList();

        block.Votes.AddRange(pendingVotes);

        Context.PosBlocks.Add(block);

        if (block.Height > 0) {
            Context.ChainState.Update(chainState);
        } else {
            Context.ChainState.Add(chainState);
        }

        Context.SaveChanges();
    }

    public void Add(List<PosBlock> blocks, ChainState chainState)
    {
        Contract.Equals(1, chainState.Id);

        Context.PosBlocks.AddRange(blocks);
        Context.ChainState.Update(chainState);

        Context.SaveChanges();
    }

    public void SaveState(ChainState chainState)
    {
        Contract.Equals(1, chainState.Id);

        Context.ChainState.Update(chainState);
        Context.SaveChanges();
    }

    public PowBlock? GetPowBlock(long height)
    {
        // TODO: get the one with most votes?
        return Context.PowBlocks
            .Where(x => x.Height == height)
            .Include(x => x.Transactions)
            .Include(x => x.Pos)
                .ThenInclude(x => x.Votes)
            .AsEnumerable()
            .GroupBy(x => x.Height, (key, values) => new 
            {
                Height = key,
                Blocks = values
            })
            .Select(x => x.Blocks.MaxBy(x => x.Pos.Votes.Count))
            .FirstOrDefault();
    }

    public PosBlock? GetPosBlock(long height)
    {
        return Context.PosBlocks
            .Where(x => x.Height == height)
            .Include(x => x.Transactions)
            .Include(x => x.Votes)
            .Include(x => x.Pow)
                .ThenInclude(x => x.Transactions)
            .AsEnumerable()
            .GroupBy(x => x.Height, (key, values) => new 
            {
                Height = key,
                Blocks = values
            })
            .Select(x => x.Blocks.MaxBy(x => x.Votes.Count))
            .FirstOrDefault();
    }

    public void Delete(PosBlock block)
    {
        Context.PosBlocks.Remove(block);
        Context.SaveChanges();
    }

    public void DeleteTransaction(Transaction tx)
    {
        Context.Transactions.Remove(tx);
        Context.SaveChanges();
    }

    public ChainState GetChainState()
    {
        return Context.ChainState
            .Include(x => x.POS)
            .Include(x => x.POW)
            .FirstOrDefault(x => x.Id == 1) ?? new ChainState();
    }

    public List<PowBlock> Tail(int count)
    {
        var start = Context.PowBlocks
            .Select(x => x.Height)
            .DefaultIfEmpty()
            .Max();

        var results = Context.PowBlocks
            .Where(x => x.Height >= start - count)
            .Include(x => x.Transactions)
            .Include(x => x.Pos)
                .ThenInclude(x => x.Votes)
            .AsEnumerable()
            .GroupBy(x => x.Height, (key, values) => new 
            {
                Height = key,
                Blocks = values
            })
            .Select(x => x.Blocks.MaxBy(x => x.Pos.Votes.Count))
            .OrderBy(x => x.Height)
            .ToList();

        return results;
    }

    public List<PowBlock> Tail(long start, int count)
    {
        var results = Context.PowBlocks
            .Where(x => x.Height < start && x.Height >= start - count)
            .Include(x => x.Transactions)
            .Include(x => x.Pos)
                .ThenInclude(x => x.Votes)
            .AsEnumerable()
            .GroupBy(x => x.Height, (key, values) => new 
            {
                Height = key,
                Blocks = values
            })
            .Select(x => x.Blocks.MaxBy(x => x.Pos.Votes.Count))
            .OrderBy(x => x.Height)
            .ToList();

        return results;
    }

    public PowBlock? Last()
    {
        return Context.PowBlocks
            .OrderByDescending(x => x.Height)
            .FirstOrDefault();
    }

    public List<PosBlock> GetPosFrom(long height)
    {
        return Context.PosBlocks
            .Where(x => x.Height > height)
            .Include(x => x.Transactions)
            .Include(x => x.Votes)
            .Include(x => x.Pow)
                .ThenInclude(x => x.Transactions)
            .ToList();
    }

    public LedgerWallet? GetWallet(Address address)
    {
        return Context.LedgerWallets
            .Where(x => x.Address == address)
            .FirstOrDefault();
    }

    public void UpdateWallet(LedgerWallet wallet)
    {
        Context.LedgerWallets.Update(wallet);
        Context.SaveChanges();
    }

    public void UpdateWallets(IEnumerable<LedgerWallet> wallets)
    {
        Context.LedgerWallets.UpdateRange(wallets);
        Context.SaveChanges();
    }

    public void AddVote(Vote vote)
    {
        Context.Votes.Add(vote);
        Context.SaveChanges();
    }

    public void AddVotes(List<Vote> votes)
    {
        Context.Votes.AddRange(votes);
        Context.SaveChanges();
    }

    public bool VoteExists(Signature signature)
    {
        // TODO this crashes, implement equals on signature?
        return Context.Votes.Any(x => x.Signature == signature);
    }

    public void Dispose()
    {
        // Context.Dispose();
    }
}
