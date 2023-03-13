using Kryolite.Shared;
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
            var db = Factory.CreateDbContext().Database;
            db.Migrate();
        }

        Context = Factory.CreateDbContext();
    }

    public long Count()
    {
        return Context.PosBlocks.Count();
    }

    public void Add(PosBlock block, ChainState chainState)
    {
        System.Diagnostics.Contracts.Contract.Equals(1, chainState.Id);

        var pendingVotes = Context.Votes
            .Where(x => x.Height == block.Height)
            .Where(x => x.Hash == block.GetHash())
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
        System.Diagnostics.Contracts.Contract.Equals(1, chainState.Id);

        Context.PosBlocks.AddRange(blocks);
        Context.ChainState.Update(chainState);

        Context.SaveChanges();
    }

    public void SaveState(ChainState chainState)
    {
        System.Diagnostics.Contracts.Contract.Equals(1, chainState.Id);

        Context.ChainState.Update(chainState);
        Context.SaveChanges();
    }

    public PowBlock? GetPowBlock(long height)
    {
        // TODO: get the one with most votes?
        return Context.PowBlocks
            .Where(x => x.Height == height)
            .Include(x => x.Transactions)
            .FirstOrDefault();
    }

    public PosBlock? GetPosBlock(long height)
    {
        return Context.PosBlocks
            .Where(x => x.Height == height)
            .Include(x => x.Transactions)
                .ThenInclude(x => x.Effects)
            .Include(x => x.Votes)
            .Include(x => x.Pow)
                .ThenInclude(x => x!.Transactions)
                    .ThenInclude(x => x.Effects)
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
            .OrderBy(x => x.Height)
            .ToList();

        return results!;
    }

    public List<PowBlock> Tail(long start, int count)
    {
        var results = Context.PowBlocks
            .Where(x => x.Height < start && x.Height >= start - count)
            .Include(x => x.Transactions)
            .OrderBy(x => x.Height)
            .ToList();

        return results!;
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
                .ThenInclude(x => x!.Transactions)
            .ToList();
    }

    public LedgerWallet? GetWallet(Address address)
    {
        return Context.LedgerWallets
            .Where(x => x.Address == address)
            //.Include(x => x.Assets)
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
        return Context.Votes.Any(x => x.Signature == signature);
    }

    public Contract? GetContract(Address address, bool noCode = false)
    {
        if (noCode)
        {
            return Context.Contracts
                .Where(x => x.Address == address)
                .Select(x => new Contract(x.Owner, x.Manifest, Array.Empty<byte>())
                    {
                        Id = x.Id,
                        Address = x.Address,
                        Balance = x.Balance,
                        Manifest = x.Manifest
                    }
                )
                .FirstOrDefault();
        }

        return Context.Contracts
            .Where(x => x.Address == address)
            .Include(x => x.Snapshots)
            .FirstOrDefault();
    }

    public List<LedgerWallet> GetRichList(int count)
    {
        return Context.LedgerWallets
            .OrderByDescending(x => x.Balance)
            .Take(count)
            .ToList();
    }

    public void AddContract(Contract contract)
    {
        Context.Contracts.Add(contract);
        Context.SaveChanges();
    }

    public void UpdateContracts(IEnumerable<Contract> contracts)
    {
        Context.Contracts.UpdateRange(contracts);
        Context.SaveChanges();
    }

    public List<Transaction> GetTransactions(Address address)
    {
        return Context.Transactions
            .Where(x => (x.From != null && x.From == address) || x.To == address)
            .ToList();
    }

    public Transaction? GetTransaction(SHA256Hash hash)
    {
        return Context.Transactions
            .Where(x => x.Hash == hash)
            .FirstOrDefault();
    }

    public void Dispose()
    {
        // Context.Dispose();
    }
}
