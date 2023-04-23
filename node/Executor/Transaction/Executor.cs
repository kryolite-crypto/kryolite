using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using node;
using Wasmtime;

namespace Kryolite.Node;

public class Executor
{
    public static ExecutorEngine<TItem, TContext> Create<TItem, TContext>(TContext context, ILogger logger) where TContext : IContext
    {
        return new ExecutorEngine<TItem, TContext>(context, logger);
    }
}

public class ExecutorEngine<TItem, TContext> where TContext : IContext
{
    private List<(BaseStep<TItem, TContext>, Func<TItem, bool>?)> Steps = new List<(BaseStep<TItem, TContext>, Func<TItem, bool>?)>();
    private TContext Context;
    private ILogger Logger;

    public ExecutorEngine(TContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutorEngine<TItem, TContext> Link<TStep>(Func<TItem, bool>? shouldExecute = null) where TStep : BaseStep<TItem, TContext>
    {
        Steps.Add((Activator.CreateInstance<TStep>(), shouldExecute));
        return this;
    }

    public bool Execute(TItem item, out ExecutionResult result)
    {
        result = ExecutionResult.UNKNOWN;

        foreach (var step in Steps.Where(step => step.Item2?.Invoke(item) ?? true)) {
            if (!step.Item1.TryExecute(item, Context, Logger, out result)) {
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
                Logger.LogError($"Transaction failed: {result}");
                continue;
            }

            valid.Add(item);
        }

        return valid;
    }    
}

public abstract class BaseStep<TItem, TContext> where TContext : IContext
{
    protected abstract void Execute(TItem item, TContext ctx, ILogger logger);
    public bool TryExecute(TItem item, TContext ctx, ILogger logger, out ExecutionResult result)
    {
        try {
            Execute(item, ctx, logger);
        } catch (ExecutionException ex) {
            result = ex.Result;
            ctx.Fail(ex);
            return false;
        } catch (Exception ex) {
            result = ExecutionResult.FAILURE;
            ctx.Fail(ex);

            logger.LogError(ex, "TryExecute failed");

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
    TIMESTAMP_IN_FUTURE,
    INVALID_CONTRACT,
    DUPLICATE_CONTRACT,
    INVALID_PAYLOAD,
    TX_IS_BLOCK_REWARD,
    NULL_PAYLOAD,
    INVALID_METHOD,
    CONTRACT_ENTRYPOINT_MISSING,
    CONTRACT_SNAPSHOT_MISSING
}

public class TransactionContext : IContext
{
    public long Height;
    public ulong Fee;
    public ulong FeeTotal;
    public long Timestamp;
    public int Seed;
    public Dictionary<string, Wallet> Wallets;
    public BlockchainRepository BlockRepository;
    public IMempoolManager MempoolManager;
    public Dictionary<string, LedgerWallet> LedgerWalletCache = new Dictionary<string, LedgerWallet>();
    public Dictionary<string, Contract> ContractCache = new Dictionary<string, Contract>();
    public List<EventArgs> Events = new List<EventArgs>();

    public Exception? Ex { get; private set; }

    public TransactionContext(BlockchainRepository blockRepository, IMempoolManager mempoolManager)
    {
        BlockRepository = blockRepository ?? throw new ArgumentNullException(nameof(blockRepository));
        MempoolManager = mempoolManager ?? throw new ArgumentNullException(nameof(mempoolManager));
        Wallets = null!;
    }

    public TransactionContext(BlockchainRepository blockRepository, Dictionary<string, Wallet> wallets)
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
    protected override void Execute(Transaction item, TransactionContext exCtx, ILogger logger)
    {
        if (item.Value != Constant.POW_REWARD) {
            throw new ExecutionException(ExecutionResult.INVALID_BLOCK_REWARD);
        }

        if (item.MaxFee > 0) {
            throw new ExecutionException(ExecutionResult.EXTRA_FEE);
        }
    }
}

public class VerifyValidatorReward : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, ILogger logger)
    {
        if (item.Value != Constant.POS_REWARD) {
            throw new ExecutionException(ExecutionResult.INVALID_BLOCK_REWARD);
        }

        if (item.MaxFee > 0) {
            throw new ExecutionException(ExecutionResult.EXTRA_FEE);
        }
    }
}

public class VerifyDevFee : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, ILogger logger)
    {
        if (item.Value != Constant.DEV_REWARD) {
            throw new ExecutionException(ExecutionResult.INVALID_BLOCK_REWARD);
        }

        if (item.MaxFee > 0) {
            throw new ExecutionException(ExecutionResult.EXTRA_FEE);
        }
    }
}

public class NotReward : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, ILogger logger)
    {
        if(item.TransactionType != TransactionType.PAYMENT && item.TransactionType != TransactionType.CONTRACT) {
            throw new ExecutionException(ExecutionResult.TX_IS_BLOCK_REWARD);
        }
    }
}

public class CheckMinFee : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, ILogger logger)
    {
        if(item.MaxFee <= 0) {
            throw new ExecutionException(ExecutionResult.LOW_FEE);
        }
    }
}

public class VerifySignature : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext exCtx, ILogger logger)
    {
        if(!item.Verify()) {
            throw new ExecutionException(ExecutionResult.SIGNATURE_VERIFICATION_FAILED);
        }
    }
}

public class FetchSenderWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
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
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if (!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            wallet = ctx.BlockRepository.GetWallet(item.To) ?? new LedgerWallet(item.To);
            ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
        }
    }
}

public class FetchContract : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if (!ctx.ContractCache.TryGetValue(item.To.ToString(), out var contract))
        {
            contract = ctx.BlockRepository.GetContract(item.To) ?? throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
            ctx.ContractCache.TryAdd(item.To.ToString(), contract);
        }
    }
}

public class AddBalanceToContract : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if (!ctx.ContractCache.TryGetValue(item.To.ToString(), out var contract))
        {
            contract = ctx.BlockRepository.GetContract(item.To) ?? throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
        }

        contract.Balance = checked(contract.Balance + item.Value);
    }
}

public class FetchOwnerWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if (!ctx.ContractCache.TryGetValue(item.To.ToString(), out var contract)) {
            throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
        }

        if (!ctx.LedgerWalletCache.TryGetValue(contract.Owner.ToString(), out var wallet)) {
            wallet = ctx.BlockRepository.GetWallet(contract.Owner) ?? new LedgerWallet(contract.Owner);
            ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
        }
    }
}

public class ExecuteContract : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if (!ctx.ContractCache.TryGetValue(item.To.ToString(), out var contract)) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
        }

        var snapshot = contract.Snapshots
            .OrderByDescending(x => x.Height)
            .FirstOrDefault();

        if (snapshot == null)
        {
            throw new ExecutionException(ExecutionResult.CONTRACT_SNAPSHOT_MISSING);
        }

        var lz4Options = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
                .WithOmitAssemblyVersion(true);

        var payload = MessagePackSerializer.Deserialize<TransactionPayload>(item.Data, lz4Options);

        if (payload.Payload is not CallMethod call)
        {
            throw new ExecutionException(ExecutionResult.INVALID_PAYLOAD);
        }

        var methodName = $"{call.Method}";
        var method = contract.Manifest.Methods
            .Where(x => x.Name == methodName)
            .FirstOrDefault();

        if (method == null)
        {
            throw new ExecutionException(ExecutionResult.INVALID_METHOD);
        }

        var methodParams = new List<object> { contract.EntryPoint ?? throw new ExecutionException(ExecutionResult.CONTRACT_ENTRYPOINT_MISSING) };

        if (call.Params is not null)
        {
            methodParams.AddRange(call.Params);
        }

        var vmContext = new VMContext(contract, item, ctx.Seed, logger);

        using var vm = KryoVM.LoadFromSnapshot(contract.Code, snapshot.Snapshot)
            .WithContext(vmContext);

        logger.LogInformation($"Executing contract {contract.Name}:{call.Method}");
        var ret = vm.CallMethod(methodName, methodParams.ToArray(), out _);
        logger.LogInformation($"Contract result = {ret}");

        if (ret != 0)
        {
            item.Effects.Clear();
            return;
        }

        var getTokenName = $"get_token";
        var hasGetToken = contract.Manifest.Methods.Any(x => x.Name == getTokenName);

        foreach (var effect in item.Effects)
        {
            if (!ctx.LedgerWalletCache.TryGetValue(effect.To.ToString(), out var wallet))
            {
                wallet = ctx.BlockRepository.GetWallet(effect.To) ?? new LedgerWallet(effect.To);
                ctx.LedgerWalletCache.TryAdd(wallet.Address.ToString(), wallet);
            }

            if (hasGetToken && effect.TokenId is not null)
            {
                var token = ctx.BlockRepository.GetToken(effect.TokenId);

                if (token is null)
                {
                    var result = vm.CallMethod(getTokenName, new object[] { contract.EntryPoint, effect.TokenId }, out var json);

                    if (result != 0)
                    {
                        logger.LogError($"get_token failed for {effect.TokenId}, error code = {result}");
                        continue;
                    }

                    if (json is null)
                    {
                        logger.LogError($"get_token failed for {effect.TokenId}, error = json  output null");
                        continue;
                    }

                    var tokenBase = JsonSerializer.Deserialize<TokenBase>(json);

                    if (tokenBase is null)
                    {
                        logger.LogError($"get_token failed for {effect.TokenId}, error = failed to parse json");
                        continue;
                    }
                     
                    token = new Token()
                    {
                        TokenId = effect.TokenId,
                        Name = tokenBase.Name,
                        Description = tokenBase.Description,
                        Contract = contract
                    };

                    ctx.BlockRepository.Context.Add(token);
                }

                token.Wallet = wallet;
                token.IsConsumed = effect.ConsumeToken;
            }

            checked
            {
                wallet.Balance += effect.Value;

                var balance = contract.Balance - effect.Value;
                if (balance < 0)
                {
                    throw new ExecutionException(ExecutionResult.TOO_LOW_BALANCE);
                }

                contract.Balance = balance;
            }
        }

        ctx.Events.AddRange(vmContext.Events);

        // TODO: take snapshot and commit at the end of block execution
        contract.Snapshots.Add(new ContractSnapshot(ctx.Height, vm.TakeSnapshot()));
    }
}

public class TakeBalanceFromSender : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        var address = item.PublicKey?.ToAddress() ?? throw new ExecutionException(ExecutionResult.INVALID_PUBLIC_KEY);
        
        if (!ctx.LedgerWalletCache.TryGetValue(address.ToString(), out var wallet)) {
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
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
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
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        wallet.Balance = checked(wallet.Balance + item.Value);
    }
}

public class AddBlockRewardToRecipient : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if(!ctx.LedgerWalletCache.TryGetValue(item.To.ToString(), out var wallet)) {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        wallet.Balance = checked(wallet.Balance + item.Value);
        wallet.Balance = checked(wallet.Balance + ctx.FeeTotal);
    }
}

public class UpdateSenderWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
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
            Recipient = item.PublicKey!.ToAddress(),
            Value = (long)item.Value * -1,
            Timestamp = ctx.Timestamp
        });
    }
}

public class UpdateRecipientWallet : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
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

public class BlockchainExContext : IContext
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

public class VerifyDifficulty : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx, ILogger logger)
    {
        if (block.Difficulty != ctx.CurrentDifficulty) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_DIFFICULTY);
        }
    }
}

public class VerifyNonce : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx, ILogger logger)
    {
        if (!block.VerifyNonce()) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_NONCE);
        }
    }
}

public class VerifyId : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx, ILogger logger)
    {
        var lastBlock = ctx.LastBlocks.Last();

        if (block.Height != lastBlock.Height + 1) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_ID);
        }
    }
}

public class VerifyParentHash : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx, ILogger logger)
    {
        var lastBlock = ctx.LastBlocks.Last();

        if (block.ParentHash != lastBlock.GetHash())
        {
            throw new ExecutionException(ExecutionResult.INVALID_PARENT_HASH);
        }
    }
}

public class VerifyTimestampPast : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx, ILogger logger)
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

public class VerifyTimestampFuture : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx, ILogger logger)
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

public class AddContract : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx, ILogger logger)
    {
        if (item.PublicKey == null)
        {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        if (item.Data is null)
        {
            throw new ExecutionException(ExecutionResult.NULL_PAYLOAD);
        }

        var lz4Options = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithOmitAssemblyVersion(true);

        var payload = MessagePackSerializer.Deserialize<TransactionPayload>(item.Data, lz4Options);

        if (payload.Payload is not NewContract newContract)
        {
            throw new ExecutionException(ExecutionResult.INVALID_PAYLOAD);
        }

        var contract = new Contract(item.PublicKey.ToAddress(), newContract.Manifest, newContract.Code);

        var ctr = ctx.BlockRepository.GetContract(contract.Address);

        if (ctr != null)
        {
            throw new ExecutionException(ExecutionResult.DUPLICATE_CONTRACT);
        }

        var vmContext = new VMContext(contract, item, ctx.Seed, logger);

        using var vm = KryoVM.LoadFromCode(contract.Code)
            .WithContext(vmContext);

        contract.EntryPoint = vm.Initialize();
        contract.Snapshots.Add(new ContractSnapshot(ctx.Height, vm.TakeSnapshot()));
        
        ctx.BlockRepository.AddContract(contract);
    }
}
