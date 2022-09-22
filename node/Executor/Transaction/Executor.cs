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

    public bool Execute(TItem item, out TransactionResult result)
    {
        result = TransactionResult.UNKNOWN;

        foreach (var step in Steps.Where(step => step.Item2?.Invoke(item) ?? true)) {
            if (!step.Item1.TryExecute(item, Context, out result)) {
                return false;
            }
        }

        return true;
    }

    public bool ExecuteBatch(IEnumerable<TItem> items, out TransactionResult result)
    {
        result = TransactionResult.UNKNOWN;

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
    public bool TryExecute(TItem item, TContext ctx, out TransactionResult result)
    {
        try {
            Execute(item, ctx);
        } catch (ExecutionException ex) {
            result = ex.Result;
            ctx.Fail(ex);
            return false;
        } catch (Exception ex) {
            result = TransactionResult.FAILURE;
            ctx.Fail(ex);
            return false;
        }

        result = TransactionResult.OK;
        return true;
    }
}

public interface IContext
{
    void Fail(Exception ex);
}

public enum TransactionResult
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
    LOW_FEE
}

public class GlobalContext : IContext
{
    public ulong Fee;
    public ulong FeeTotal;
    public long Timestamp;
    public Dictionary<string, Wallet> Wallets;
    public LedgerRepository LedgerRepository;
    public IMempoolManager MempoolManager;
    public Dictionary<string, LedgerWallet> LedgerWalletCache = new Dictionary<string, LedgerWallet>();

    public Exception? Ex { get; private set; }

    public GlobalContext(LedgerRepository ledgerRepository, IMempoolManager mempoolManager)
    {
        LedgerRepository = ledgerRepository ?? throw new ArgumentNullException(nameof(ledgerRepository));
        MempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        Wallets = null!;
    }

    public GlobalContext(LedgerRepository ledgerRepository, Dictionary<string, Wallet> wallets)
    {
        LedgerRepository = ledgerRepository ?? throw new ArgumentNullException(nameof(ledgerRepository));
        Wallets = wallets ?? throw new ArgumentNullException(nameof(wallets));
        MempoolManager = null!;
    }

    public void Fail(Exception ex)
    {
        Ex = ex;
    }
}

public class TransactionContext
{
    public LedgerWallet? From;
    public LedgerWallet? To;
}

public class ExecutionException : Exception
{
    public TransactionResult Result { get; }
    public ExecutionException(TransactionResult result)
    {
        Result = result;
    }
}

public class VerifyBlockReward : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext exCtx)
    {
        if (item.Value != 750000000) {
            throw new ExecutionException(TransactionResult.INVALID_BLOCK_REWARD);
        }
    }
}

public class VerifyValidatorReward : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext exCtx)
    {
        if (item.Value != 200000000) {
            throw new ExecutionException(TransactionResult.INVALID_BLOCK_REWARD);
        }
    }
}

public class VerifyDevFee : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext exCtx)
    {
        if (item.Value != 50000000) {
            throw new ExecutionException(TransactionResult.INVALID_BLOCK_REWARD);
        }
    }
}

public class NotReward : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext exCtx)
    {
        if(item.TransactionType != TransactionType.PAYMENT) {
            throw new ExecutionException(TransactionResult.FAILURE);
        }
    }
}

public class CheckMinFee : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext exCtx)
    {
        if(item.MaxFee <= 0) {
            throw new ExecutionException(TransactionResult.LOW_FEE);
        }
    }
}

public class VerifySignature : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext exCtx)
    {
        if(!item.Verify()) {
            throw new ExecutionException(TransactionResult.SIGNATURE_VERIFICATION_FAILED);
        }
    }
}

public class FetchSenderWallet : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        var from = item.PublicKey ?? throw new ExecutionException(TransactionResult.INVALID_PUBLIC_KEY);
        var wallet = ctx.LedgerRepository.GetWallet(from.ToAddress()) ?? throw new ExecutionException(TransactionResult.INVALID_SENDER);

        ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
    }
}

public class FetchRecipientWallet : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        var wallet = ctx.LedgerRepository.GetWallet(item.To) ?? new LedgerWallet(item.To);
        ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
    }
}

public class TakeBalanceFromSender : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        var address = item.PublicKey?.ToAddress() ?? throw new ExecutionException(TransactionResult.INVALID_PUBLIC_KEY);
        
        if(!ctx.LedgerWalletCache.TryGetValue(address.ToString(), out var wallet)) {
            throw new ExecutionException(TransactionResult.INVALID_SENDER);
        }

        if (wallet.Balance < checked(item.Value + ctx.Fee)) {
            throw new ExecutionException(TransactionResult.TOO_LOW_BALANCE);
        }

        wallet.Balance = checked(wallet.Balance - (item.Value + ctx.Fee));
    }
}

public class HasFunds : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        var address = item.PublicKey?.ToAddress() ?? throw new ExecutionException(TransactionResult.INVALID_PUBLIC_KEY);
        
        if(!ctx.LedgerWalletCache.TryGetValue(address.ToString(), out var wallet)) {
            throw new ExecutionException(TransactionResult.INVALID_SENDER);
        }

        var pending = ctx.MempoolManager.GetPending(address);

        if (wallet.Balance < checked(item.Value + item.MaxFee + pending)) {
            throw new ExecutionException(TransactionResult.TOO_LOW_BALANCE);
        }
    }
}

public class AddBalanceToRecipient : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            throw new ExecutionException(TransactionResult.INVALID_SENDER);
        }

        wallet.Balance = checked(wallet.Balance + item.Value);
    }
}

public class AddBlockRewardToRecipient : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            throw new ExecutionException(TransactionResult.INVALID_SENDER);
        }

        wallet.Balance = checked(wallet.Balance + ctx.FeeTotal);
    }
}

public class UpdateSenderWallet : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        var address = item.PublicKey ?? throw new ExecutionException(TransactionResult.INVALID_PUBLIC_KEY);
        
        if(!ctx.LedgerWalletCache.TryGetValue(address.ToAddress().ToString(), out var ledgerWallet)) {
            throw new ExecutionException(TransactionResult.INVALID_SENDER);
        }

        if (!ctx.Wallets.TryGetValue(ledgerWallet.Address.ToString(), out var wallet)) {
            return;
        }

        wallet.Updated = true;
        wallet.Balance = ledgerWallet.Balance;
        wallet.WalletTransactions.Add(new WalletTransaction
        {
            Recipient = item.To,
            Value = (long)item.Value * -1,
            Timestamp = ctx.Timestamp
        });
    }
}

public class UpdateRecipientWallet : BaseStep<Transaction, GlobalContext>
{
    protected override void Execute(Transaction item, GlobalContext ctx)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var ledgerWallet)) {
            throw new ExecutionException(TransactionResult.INVALID_SENDER);
        }

        if (!ctx.Wallets.TryGetValue(ledgerWallet.Address.ToString(), out var wallet)) {
            return;
        }

        wallet.Updated = true;
        wallet.Balance = ledgerWallet.Balance;
        wallet.WalletTransactions.Add(new WalletTransaction
        {
            Recipient = item.PublicKey?.ToAddress() ?? item.To,
            Value = (long)item.Value,
            Timestamp = ctx.Timestamp
        });
    }
}
