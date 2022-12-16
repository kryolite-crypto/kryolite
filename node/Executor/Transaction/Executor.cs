using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class Executor
{
    public static ExecutorEngine<TItem, TContext> Create<TItem, TContext>(TContext context) where TContext : IContext
    {
        return new ExecutorEngine<TItem, TContext>(context);
    }
}

public class ExecutorEngine<TItem, TContext> where TContext : IContext
{
    private List<(BaseStep<TItem, TContext>, Func<TItem, bool>?)> Steps = new List<(BaseStep<TItem, TContext>, Func<TItem, bool>?)>();
    private TContext Context;

    public ExecutorEngine(TContext context) => Context = context ?? throw new ArgumentNullException(nameof(context));

    public ExecutorEngine<TItem, TContext> Link<TStep>(Func<TItem, bool>? shouldExecute = null) where TStep : BaseStep<TItem, TContext>
    {
        Steps.Add((Activator.CreateInstance<TStep>(), shouldExecute));
        return this;
    }

    public bool Execute(TItem item, out ExecutionResult result)
    {
        result = ExecutionResult.UNKNOWN;

        foreach (var step in Steps.Where(step => step.Item2?.Invoke(item) ?? true)) {
            if (!step.Item1.TryExecute(item, Context, out result)) {
                return false;
            }
        }

        return true;
    }

    public bool ExecuteBatch(IEnumerable<TItem> items, out ExecutionResult result)
    {
        result = ExecutionResult.UNKNOWN;

        foreach (var item in items) {
            if(!Execute(item, out result)) {
                return false;
            }
        }

        return true;
    }

    public List<TItem> Execute(IEnumerable<TItem> items)
    {
        var valid = new List<TItem>();

        foreach (var item in items) {
            if(!Execute(item, out var result)) {
                Console.WriteLine($"Transaction failed {result}");
                continue;
            }

            valid.Add(item);
        }

        return valid;
    }    
}

public abstract class BaseStep<TItem, TContext> where TContext : IContext
{
    protected abstract void Execute(TItem item, TContext ctx);
    public bool TryExecute(TItem item, TContext ctx, out ExecutionResult result)
    {
        try {
            Execute(item, ctx);
        } catch (ExecutionException ex) {
            result = ex.Result;
            ctx.Fail(ex);
            return false;
        } catch (Exception ex) {
            result = ExecutionResult.FAILURE;
            ctx.Fail(ex);
            return false;
        }

        result = ExecutionResult.OK;
        return true;
    }
}

public interface IContext
{
    void Fail(Exception ex);
}

public enum ExecutionResult
{
    OK,
    FAILURE,
    UNKNOWN,
    SIGNATURE_VERIFICATION_FAILED,
    INVALID_PUBLIC_KEY,
    INVALID_SENDER,
    TOO_LOW_BALANCE,
    INVALID_BLOCK_REWARD,
    INVALID_VALIDATOR_REWARD,
    INVALID_DEV_FEE,
    LOW_FEE,
    EXTRA_FEE,
    INVALID_DIFFICULTY,
    INVALID_NONCE,
    INVALID_ID,
    INVALID_PARENT_HASH,
    TIMESTAMP_TOO_OLD,
    TIMESTAMP_IN_FUTURE
}

public class TransactionContext : IContext
{
    public ulong Fee;
    public ulong FeeTotal;
    public long Timestamp;
    public Dictionary<string, Wallet> Wallets;
    public BlockRepository BlockRepository;
    public IMempoolManager MempoolManager;
    public Dictionary<string, LedgerWallet> LedgerWalletCache = new Dictionary<string, LedgerWallet>();

    public Exception? Ex { get; private set; }

    public TransactionContext(BlockRepository blockRepository, IMempoolManager mempoolManager)
    {
        BlockRepository = blockRepository ?? throw new ArgumentNullException(nameof(blockRepository));
        MempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        Wallets = null!;
    }

    public TransactionContext(BlockRepository blockRepository, Dictionary<string, Wallet> wallets)
    {
        BlockRepository = blockRepository ?? throw new ArgumentNullException(nameof(blockRepository));
        Wallets = wallets ?? throw new ArgumentNullException(nameof(wallets));
        MempoolManager = null!;
    }

    public void Fail(Exception ex)
    {
        Ex = ex;
    }
}

public class ExecutionException : Exception
{
    public ExecutionResult Result { get; }
    public ExecutionException(ExecutionResult result)
    {
        Result = result;
    }
}

public class VerifyBlockReward : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx)
    {
        if (item.Value != 750000000) {
            throw new ExecutionException(ExecutionResult.INVALID_BLOCK_REWARD);
        }

        if (item.MaxFee > 0) {
            throw new ExecutionException(ExecutionResult.EXTRA_FEE);
        }
    }
}

public class VerifyValidatorReward : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx)
    {
        if (item.Value != 200000000) {
            throw new ExecutionException(ExecutionResult.INVALID_BLOCK_REWARD);
        }

        if (item.MaxFee > 0) {
            throw new ExecutionException(ExecutionResult.EXTRA_FEE);
        }
    }
}

public class VerifyDevFee : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx)
    {
        if (item.Value != 50000000) {
            throw new ExecutionException(ExecutionResult.INVALID_BLOCK_REWARD);
        }

        if (item.MaxFee > 0) {
            throw new ExecutionException(ExecutionResult.EXTRA_FEE);
        }
    }
}

public class NotReward : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx)
    {
        if(item.TransactionType != TransactionType.PAYMENT) {
            throw new ExecutionException(ExecutionResult.FAILURE);
        }
    }
}

public class CheckMinFee : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx)
    {
        if(item.MaxFee <= 0) {
            throw new ExecutionException(ExecutionResult.LOW_FEE);
        }
    }
}

public class VerifySignature : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx)
    {
        if(!item.Verify()) {
            throw new ExecutionException(ExecutionResult.SIGNATURE_VERIFICATION_FAILED);
        }
    }
}

public class FetchSenderWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        var from = item.PublicKey ?? throw new ExecutionException(ExecutionResult.INVALID_PUBLIC_KEY);

        if (!ctx.LedgerWalletCache.TryGetValue(from.ToAddress().ToString(), out var _)) {
            var wallet = ctx.BlockRepository.GetWallet(from.ToAddress()) ?? throw new ExecutionException(ExecutionResult.INVALID_SENDER);
            ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
        }
    }
}

public class FetchRecipientWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        if (!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            wallet = ctx.BlockRepository.GetWallet(item.To) ?? new LedgerWallet(item.To);
            ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
        }
    }
}

public class TakeBalanceFromSender : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        var address = item.PublicKey?.ToAddress() ?? throw new ExecutionException(ExecutionResult.INVALID_PUBLIC_KEY);
        
        if(!ctx.LedgerWalletCache.TryGetValue(address.ToString(), out var wallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        if (wallet.Balance < checked(item.Value + ctx.Fee)) {
            throw new ExecutionException(ExecutionResult.TOO_LOW_BALANCE);
        }

        wallet.Balance = checked(wallet.Balance - (item.Value + ctx.Fee));
    }
}

public class HasFunds : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        var address = item.PublicKey?.ToAddress() ?? throw new ExecutionException(ExecutionResult.INVALID_PUBLIC_KEY);
        
        if(!ctx.LedgerWalletCache.TryGetValue(address.ToString(), out var wallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        var pending = ctx.MempoolManager.GetPending(address);

        if (wallet.Balance < checked(item.Value + item.MaxFee + pending)) {
            throw new ExecutionException(ExecutionResult.TOO_LOW_BALANCE);
        }

        wallet.Balance -= checked(item.Value + item.MaxFee + pending);
    }
}

public class AddBalanceToRecipient : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        wallet.Balance = checked(wallet.Balance + item.Value);
    }
}

public class AddBlockRewardToRecipient : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        wallet.Balance = checked(wallet.Balance + ctx.FeeTotal);
    }
}

public class UpdateSenderWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        var address = item.PublicKey ?? throw new ExecutionException(ExecutionResult.INVALID_PUBLIC_KEY);
        
        if(!ctx.LedgerWalletCache.TryGetValue(address.ToAddress().ToString(), out var ledgerWallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        if (!ctx.Wallets.TryGetValue(ledgerWallet.Address.ToString(), out var wallet)) {
            return;
        }

        wallet.Updated = true;
        wallet.Balance = ledgerWallet.Balance;
        wallet.WalletTransactions.Add(new WalletTransaction
        {
            Recipient = item.PublicKey!.Value.ToAddress(),
            Value = (long)item.Value * -1,
            Timestamp = ctx.Timestamp
        });
    }
}

public class UpdateRecipientWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var ledgerWallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        if (!ctx.Wallets.TryGetValue(ledgerWallet.Address.ToString(), out var wallet)) {
            return;
        }

        wallet.Updated = true;
        wallet.Balance = ledgerWallet.Balance;
        wallet.WalletTransactions.Add(new WalletTransaction
        {
            Recipient = item.To,
            Value = (long)item.Value,
            Timestamp = ctx.Timestamp
        });
    }
}

public class BlockchainContext : IContext
{
    public List<PowBlock> LastBlocks { get; init; } = new List<PowBlock>();
    public DateTimeOffset NetworkTime { get; init; }
    public Difficulty CurrentDifficulty { get; set; }

    public Exception? Ex { get; private set; }

    public void Fail(Exception ex)
    {
        Ex = ex;
    }
}

public class VerifyDifficulty : BaseStep<PowBlock, BlockchainContext>
{
    protected override void Execute(PowBlock block, BlockchainContext ctx)
    {
        if (block.Difficulty != ctx.CurrentDifficulty) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_DIFFICULTY);
        }
    }
}

public class VerifyNonce : BaseStep<PowBlock, BlockchainContext>
{
    protected override void Execute(PowBlock block, BlockchainContext ctx)
    {
        if (!block.VerifyNonce()) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_NONCE);
        }
    }
}

public class VerifyId : BaseStep<PowBlock, BlockchainContext>
{
    protected override void Execute(PowBlock block, BlockchainContext ctx)
    {
        var lastBlock = ctx.LastBlocks.Last();

        if (block.Height != lastBlock.Height + 1) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_ID);
        }
    }
}

public class VerifyParentHash : BaseStep<PowBlock, BlockchainContext>
{
    protected override void Execute(PowBlock block, BlockchainContext ctx)
    {
        var lastBlock = ctx.LastBlocks.Last();

        if (!Enumerable.SequenceEqual((byte[])block.ParentHash, (byte[])lastBlock.GetHash()))
        {
            throw new ExecutionException(ExecutionResult.INVALID_PARENT_HASH);
        }
    }
}

public class VerifyTimestampPast : BaseStep<PowBlock, BlockchainContext>
{
    protected override void Execute(PowBlock block, BlockchainContext ctx)
    {
        // Get median of last 11 blocks
        var median = ctx.LastBlocks.TakeLast(11)
            .ElementAt((int)(Math.Min(ctx.LastBlocks.Count / 2, 5)));

        if (block.Timestamp < median.Timestamp)
        {
            throw new ExecutionException(ExecutionResult.TIMESTAMP_TOO_OLD);
        }
    }
}

public class VerifyTimestampFuture : BaseStep<PowBlock, BlockchainContext>
{
    protected override void Execute(PowBlock block, BlockchainContext ctx)
    {
        // Get median of last 11 blocks
        var median = ctx.LastBlocks.TakeLast(11)
            .ElementAt((int)(Math.Min(ctx.LastBlocks.Count / 2, 5)));

        if (block.Timestamp > ctx.NetworkTime.AddHours(2).ToUnixTimeSeconds())
        {
            throw new ExecutionException(ExecutionResult.TIMESTAMP_IN_FUTURE);
        }
    }
}
