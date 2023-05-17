using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kryolite.Node;

public class BlockchainRepository : IDisposable
{
    public BlockchainContext Context { get; private set; }

    private static PooledDbContextFactory<BlockchainContext>? Factory { get; set; }

    public BlockchainRepository()
    {
        if (Factory is null)
        {
            var walletPath = Path.Join(BlockchainService.DATA_PATH, "blocks.dat");

            var options = new DbContextOptionsBuilder<BlockchainContext>()
                .UseSqlite($"Data Source={walletPath}")
                //.EnableSensitiveDataLogging()
                //.LogTo(Console.WriteLine)
                .Options;

            Factory = new PooledDbContextFactory<BlockchainContext>(options);
            var ctx = Factory.CreateDbContext();
            //db.Migrate();
            ctx.Database.EnsureDeleted();
            ctx.Database.EnsureCreated();
        }

        Context = Factory.CreateDbContext();

        FormattableString cmd = $@"
pragma threads = 4;
pragma journal_mode = wal; 
pragma synchronous = normal;
pragma temp_store = default; 
pragma mmap_size = -1;
";

        Context.Database.ExecuteSql(cmd);
    }

    public long Count()
    {
        return Context.Blocks.Count();
    }

    public T? Get<T>(SHA256Hash transactionId) where T : Transaction
    {
        return Context.Set<T>()
            .Where(x => x.TransactionId == transactionId)
            .FirstOrDefault();
    }

    public void Add<T>(T tx) where T : Transaction
    {
        Context.Update<T>(tx);
        Context.SaveChanges();
    }

    public Genesis GetGenesis()
    {
        return Context.Genesis.First();
    }

    public View GetLastView()
    {
        return Context.Views
            .OrderByDescending(x => x.Height)
            .Include(x => x.Votes)
            .First();
    }

    public List<Vote> GetVotes(SHA256Hash transactionId)
    {
        return Context.Votes
            .Where(x => x.TransactionId == transactionId)
            .ToList();
    }

    /*public void Add(List<Transaction> txs)
    {
        Context.Transactions.AddRange(txs);
        Context.SaveChanges();
    }*/

    public void SaveState(ChainState chainState)
    {
        Context.Update(chainState);
        Context.SaveChanges();
    }

    public Block GetBlockAt(int skip)
    {
        return Context.Blocks
            .OrderByDescending(x => x.Timestamp)
            .Skip(skip)
            .First();
    }

    public List<Block> GetBlocks(long height)
    {
        return Context.Blocks
            .Where(x => x.Height == height)
            .Include(x => x.Validates)
            .ToList();
    }

    /*private static readonly Func<BlockchainContext, long, PosBlock?> QueryPosBlock =
        EF.CompileQuery((BlockchainContext context, long height) =>
            context.PosBlocks
                .Where(x => x.Height == height)
                .Include(x => x.Votes)
                .Include(x => x.Pow)
                    .ThenInclude(x => x!.Transactions)
                        .ThenInclude(x => x.Effects)
                .OrderByDescending(x => x.Votes.Count)
                .FirstOrDefault());

    public PosBlock? GetPosBlock(long height)
    {
        return QueryPosBlock(Context, height);
    }*/

    public void Delete(Transaction tx)
    {
        Context.Transactions.Remove(tx);
        Context.SaveChanges();
    }

    public void DeleteContractSnapshot(long height)
    {
        var snapshots = Context.ContractSnapshots.Where(x => x.Height > height);

        Context.ContractSnapshots.RemoveRange(snapshots);
        Context.SaveChanges();
    }

    public ChainState GetChainState()
    {
        return Context.ChainState.First(x => x.Id == 1);
    }

    /*public List<PowBlock> Tail(int count)
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
            .Include(x => x.Votes)
            .Include(x => x.Pow)
                .ThenInclude(x => x!.Transactions)
            .ToList();
    }

    public List<PowBlock> GetPowFrom(long height)
    {
        return Context.PowBlocks
            .Where(x => x.Height > height)
            .ToList();
    }*/

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

    public void AddVote(List<Vote> votes)
    {
        Context.Votes.AddRange(votes);
        Context.SaveChanges();
    }

    public bool VoteExists(Signature signature)
    {
        return Context.Votes
            .Any(x => x.Signature == signature);
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

    public void UpdateTokens(IEnumerable<Token> tokens)
    {
        Context.Tokens.UpdateRange(tokens);
        Context.SaveChanges();
    }

    public List<Transaction> GetTransactions(Address address)
    {
        return Context.Transactions
            .Where(x => (x.From != null && x.From == address) || x.To == address)
            .ToList();
    }

    public Transaction? GetTransaction(SHA256Hash transactionId)
    {
        return Context.Transactions
            .Where(x => x.TransactionId == transactionId)
            .FirstOrDefault();
    }

    public List<Transaction> GetTransactionsToValidate()
    {
        var transactions = Context.Transactions
            .Where(x => x.Height == null)
            .ToList();

        if (transactions.Count == 1)
        {
            var tx = Context.Transactions
                .OrderByDescending(x => x.Height)
                .First();

            transactions.Add(tx);
        }

        return transactions;
    }

    public Token? GetToken(SHA256Hash tokenId)
    {
        return Context.Tokens
            .Where(x => x.TokenId == tokenId)
            .Include(x => x.Wallet)
            .FirstOrDefault();
    }

    public Token? GetToken(Address from, SHA256Hash tokenId)
    {
        return Context.Tokens
            .Where(x => x.Wallet.Address == from && x.TokenId == tokenId)
            .Include(x => x.Wallet)
            .FirstOrDefault();
    }

    public List<Token> GetTokens(Address from)
    {
        return Context.Tokens
            .Where(x => x.Wallet.Address == from)
            .Include(x => x.Wallet)
            .ToList();
    }

    public List<Token> GetContractTokens(Address contractAddress)
    {
        return Context.Tokens
            .Where(x => x.Contract.Address == contractAddress)
            .Include(x => x.Wallet)
            .ToList();
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}
