using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;
using Microsoft.Extensions.Logging;

namespace Marccacoin;

public class Executor
{
    public static ExecutorEngine<TItem, TExecutionContext, TContext> Create<TItem, TExecutionContext, TContext>(TContext context) where TContext : IContext
    {
        return new ExecutorEngine<TItem, TExecutionContext, TContext>(context);
    }
}

public class ExecutorEngine<TItem, TItemContext, TContext> where TContext : IContext
{
    private List<(BaseStep<TItem, TItemContext, TContext>, Func<TItem, bool>?)> Steps = new List<(BaseStep<TItem, TItemContext, TContext>, Func<TItem, bool>?)>();
    private TContext Context;

    public ExecutorEngine(TContext context) => Context = context ?? throw new ArgumentNullException(nameof(context));

    public ExecutorEngine<TItem, TItemContext, TContext> Link<TStep>(Func<TItem, bool>? shouldExecute = null) where TStep : BaseStep<TItem, TItemContext, TContext>
    {
        Steps.Add((Activator.CreateInstance<TStep>(), shouldExecute));
        return this;
    }

    public bool Execute(TItem item, out TransactionResult result)
    {
        result = TransactionResult.UNKNOWN;
        
        var ctx = Activator.CreateInstance<TItemContext>();

        foreach (var step in Steps.Where(step => step.Item2?.Invoke(item) ?? true)) {
            if (!step.Item1.TryExecute(item, ctx, Context, out result)) {
                return false;
            }
        }

        return true;
    }

    public bool Execute(List<TItem> items, out TransactionResult result)
    {
        result = TransactionResult.UNKNOWN;

        foreach (var item in items) {
            if(!Execute(item, out result)) {
                return false;
            }
        }

        return true;
    }
}

public abstract class BaseStep<TItem, TItemContext, TContext> where TContext : IContext
{
    protected abstract void Execute(TItem item, TItemContext exCtx, TContext ctx);
    public bool TryExecute(TItem item, TItemContext exCtx, TContext ctx, out TransactionResult result)
    {
        try {
            Execute(item, exCtx, ctx);
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
    INVALID_DEV_FEE
}

public class GlobalContext : IContext
{
    public ulong Fee;
    public ulong FeeTotal;
    public long Timestamp;
    public Dictionary<string, Wallet> Wallets;
    public LedgerRepository LedgerRepository;
    public HashSet<LedgerWallet> UpdatedLedgerWallets = new HashSet<LedgerWallet>();
    public HashSet<Wallet> UpdatedWallets = new HashSet<Wallet>();

    public Exception? Ex { get; private set; }

    public GlobalContext(LedgerRepository ledgerRepository, Dictionary<string, Wallet> wallets)
    {
        LedgerRepository = ledgerRepository ?? throw new ArgumentNullException(nameof(ledgerRepository));
        Wallets = wallets ?? throw new ArgumentNullException(nameof(wallets));
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

public class VerifyBlockReward : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, GlobalContext exCtx)
    {
        if (item.Value != 750000000) {
            throw new ExecutionException(TransactionResult.INVALID_BLOCK_REWARD);
        }
    }
}

public class VerifyValidatorReward : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, GlobalContext exCtx)
    {
        if (item.Value != 200000000) {
            throw new ExecutionException(TransactionResult.INVALID_BLOCK_REWARD);
        }
    }
}

public class VerifyDevFee : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, GlobalContext exCtx)
    {
        if (item.Value != 50000000) {
            throw new ExecutionException(TransactionResult.INVALID_BLOCK_REWARD);
        }
    }
}

public class VerifySignature : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, GlobalContext exCtx)
    {
        if(!item.Verify()) {
            throw new ExecutionException(TransactionResult.SIGNATURE_VERIFICATION_FAILED);
        }
    }
}

public class FetchSenderWallet : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        var from = item.PublicKey ?? throw new ExecutionException(TransactionResult.INVALID_PUBLIC_KEY);
        exCtx.From = ctx.LedgerRepository.GetWallet(from.ToAddress()) ?? throw new ExecutionException(TransactionResult.INVALID_SENDER);
    }
}

public class FetchRecipientWallet : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        exCtx.To = ctx.LedgerRepository.GetWallet(item.To) ?? new LedgerWallet(item.To);
    }
}

public class TakeBalanceFromSender : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        if (exCtx.From!.Balance < checked(item.Value + ctx.Fee)) {
            throw new ExecutionException(TransactionResult.TOO_LOW_BALANCE);
        }

        exCtx.From.Balance = checked(exCtx.From.Balance - (item.Value + ctx.Fee));
        ctx.UpdatedLedgerWallets.Add(exCtx.From);
    }
}

public class AddBalanceToRecipient : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        exCtx.To!.Balance = checked(exCtx.To.Balance + item.Value);
        ctx.UpdatedLedgerWallets.Add(exCtx.To);
    }
}

public class AddBlockRewardToRecipient : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        exCtx.To!.Balance = checked(exCtx.To.Balance + ctx.FeeTotal);
    }
}

public class UpdateSenderWallet : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        if (!ctx.Wallets.TryGetValue(exCtx.From!.Address.ToString(), out var wallet)) {
            return;
        }

        wallet.Balance = exCtx.From.Balance;
        wallet.WalletTransactions.Add(new WalletTransaction
        {
            Recipient = item.To,
            Value = (long)item.Value * -1,
            Timestamp = ctx.Timestamp
        });

        ctx.UpdatedWallets.Add(wallet);
    }
}

public class UpdateRecipientWallet : BaseStep<Transaction, TransactionContext, GlobalContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, GlobalContext ctx)
    {
        if (!ctx.Wallets.TryGetValue(exCtx.To!.Address.ToString(), out var wallet)) {
            return;
        }

        wallet.Balance = exCtx.To.Balance;
        wallet.WalletTransactions.Add(new WalletTransaction
        {
            Recipient = item.To,
            Value = (long)item.Value,
            Timestamp = ctx.Timestamp
        });

        ctx.UpdatedWallets.Add(wallet);
    }
}
