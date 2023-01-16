using System.Text;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using MessagePack;
using Microsoft.Extensions.Logging;
using Wasmtime;

namespace Kryolite.Node;

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
                Console.WriteLine($"Transaction failed: {result}");
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

            Console.WriteLine(ex);

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
    INVALID_METHOD
}

public class TransactionContext : IContext
{
    public ulong Fee;
    public ulong FeeTotal;
    public long Timestamp;
    public Dictionary<string, Wallet> Wallets;
    public BlockchainRepository BlockRepository;
    public IMempoolManager MempoolManager;
    public Dictionary<string, LedgerWallet> LedgerWalletCache = new Dictionary<string, LedgerWallet>();
    public Dictionary<string, Contract> ContractCache = new Dictionary<string, Contract>();

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
        if(item.TransactionType != TransactionType.PAYMENT && item.TransactionType != TransactionType.CONTRACT) {
            throw new ExecutionException(ExecutionResult.TX_IS_BLOCK_REWARD);
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

public class FetchContract : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
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
    protected override void Execute(Transaction item, TransactionContext ctx)
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
    protected override void Execute(Transaction item, TransactionContext ctx)
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
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        if (!ctx.ContractCache.TryGetValue(item.To.ToString(), out var contract)) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
        }

        using var engine = new Engine(new Config()
            .WithFuelConsumption(true)
            .WithReferenceTypes(true));

        var errors = Module.Validate(engine, contract.Code);
        if(errors != null)
        {
            Console.WriteLine(errors);
            throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
        }

        using var module = Module.FromBytes(engine, contract.Name, contract.Code);

        var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
        var payload = MessagePackSerializer.Deserialize<TransactionPayload>(item.Data, lz4Options);

        if (payload.Payload is not CallMethod call)
        {
            throw new ExecutionException(ExecutionResult.INVALID_PAYLOAD);
        }

        var methodName = $"_c_{call.Method}";

        if (!module.Exports.Any(x => x.Name == methodName))
        {
            throw new ExecutionException(ExecutionResult.INVALID_METHOD);
        }

        if (!ctx.LedgerWalletCache.TryGetValue(contract.Owner.ToString(), out var ownerWallet))
        {
            var wallet = ctx.BlockRepository.GetWallet(contract.Owner);
            if (wallet != null) 
            {
                ctx.LedgerWalletCache.Add(contract.Owner.ToString(), wallet);
            }
        }

        using var linker = new Linker(engine);
        using var store = new Store(engine);

        ulong start = 1000000;
        store.AddFuel(start + item.MaxFee);

        linker.Define("kryolite", "get_balance",
            Function.FromCallback<int, long>(store,  (Caller caller, int address) => 
            {
                var memory = caller.GetMemory("memory");

                if (memory is null)
                {
                    return 0;
                }

                var addr = memory.ReadAddress(address);

                if (addr.IsContract())
                {
                    if(!ctx.ContractCache.TryGetValue(addr.ToString(), out var ctract))
                    {
                        return 0;
                    }

                    Console.WriteLine($"Get balance for '{addr.ToString()}': {ctract.Balance / 1000000} kryo");
                    return (long)ctract.Balance;
                }

                if(!ctx.LedgerWalletCache.TryGetValue(addr.ToString(), out var wallet))
                {
                    return 0;
                }

                Console.WriteLine($"Get balance for '{addr.ToString()}': {wallet.Balance / 1000000} kryo");

                return (long)wallet.Balance;
            })
        );

        linker.Define("kryolite", "transfer",
            Function.FromCallback<int, long>(store,  (Caller caller, int address, long value) => 
            {
                var memory = caller.GetMemory("memory");

                if (memory is null)
                {
                    return;
                }

                var addr = memory.ReadAddress(address);

                if (addr.Equals(contract.Address))
                {
                    Console.WriteLine($"Cannot transfer to contract address");
                    return;
                }

                if(!ctx.LedgerWalletCache.TryGetValue(addr.ToString(), out var wallet))
                {
                    wallet = ctx.BlockRepository.GetWallet(addr);
                    
                    if (wallet == null) 
                    {
                        wallet = new LedgerWallet(addr);
                    }

                    ctx.LedgerWalletCache.Add(wallet.Address.ToString(), wallet);
                }

                Console.WriteLine($"Set balance for '{addr.ToString()}': {wallet.Balance / 1000000} -> {(wallet.Balance + (ulong)value) / 1000000} kryo");
                contract.Balance = checked(contract.Balance - (ulong)value);
                wallet.Balance = checked(wallet.Balance + (ulong)value);

                item.Effects.Add(new Effect(addr, (ulong)value));
            })
        );

        linker.Define("kryolite", "get_state_sz",
            Function.FromCallback<int>(store,  (Caller caller) => 
            {
                return contract.State.Length * 2;
            })
        );

        linker.Define("kryolite", "get_state",
            Function.FromCallback<int>(store,  (Caller caller, int ptr) => 
            {
                caller.GetMemory("memory")!.WriteString(ptr, contract.State, Encoding.Unicode);
            })
        );

        linker.Define("kryolite", "set_state",
            Function.FromCallback<int>(store,  (Caller caller, int ptr) => 
            {
                var memory = caller.GetMemory("memory");

                if (memory is null)
                {
                    return;
                }

                var keyLen = memory.ReadInt32(ptr - 4);
                var state = memory.ReadString(ptr, keyLen, Encoding.Unicode);

                Console.WriteLine(state);
                contract.State = state;
            })
        );

        linker.Define(
            "env",
            "console.log",
            Function.FromCallback(store, (Caller caller, int message) =>
            {
                var mem = caller.GetMemory("memory");

                if (mem is null)
                {
                    return;
                }

                var msgLen = mem.ReadInt32(message - 4);
                var msg = mem.ReadString(message, msgLen, Encoding.Unicode);
                Console.WriteLine("LOG: " + msg);
            })
        );

        linker.Define(
            "env",
            "abort",
            Function.FromCallback(store, (Caller caller, int message, int filename, int linenum, int colnum) =>
            {
                var mem = caller.GetMemory("memory");

                if (mem is null)
                {
                    return;
                }

                var filenameStr = string.Empty;
                var messageStr = string.Empty;

                if (filename > 0) 
                {
                    filenameStr = mem.ReadString(filename, mem.ReadInt32(filename - 4), Encoding.Unicode);
                }

                if (message > 0)
                {
                    messageStr = mem.ReadString(message, mem.ReadInt32(message - 4), Encoding.Unicode);
                }

                throw new Exception($"{messageStr} ({filenameStr}:{linenum}:{colnum})");
            })
        );

        linker.Define(
            "env",
            "seed",
            Function.FromCallback(store, (Caller caller) =>
            {
                return ctx.Timestamp;
            })
        );

        linker.Define(
            "env",
            "process.exit",
            Function.FromCallback<int>(store, (Caller caller, int exitCode) =>
            {
                throw new ExitException(exitCode);
            })
        );

        var instance = linker.Instantiate(store, module);

        var memory = instance.GetMemory("memory") ?? throw new Exception("memory not found");
        var tx = (int)(instance.GetGlobal("Transaction")?.GetValue() ?? throw new Exception("Transaction global not found"));

        memory.WriteBuffer(memory.ReadInt32(tx), item.PublicKey!.Value.ToAddress());
        memory.WriteBuffer(memory.ReadInt32(tx + 4), item.To);
        memory.WriteInt64(tx + 8, (long)item.Value);

        var ctr = (int)(instance.GetGlobal("Contract")?.GetValue() ?? throw new Exception("Contract global not found"));
        memory.WriteBuffer(memory.ReadInt32(ctr), contract.Owner);
        memory.WriteBuffer(memory.ReadInt32(ctr + 4), contract.Address);

        var run = instance.GetAction(methodName);

        if (run == null)
        {
            throw new ExecutionException(ExecutionResult.FAILURE);
        }

        var exitCode = 0;
        Console.WriteLine($"Executing contract {contract.Name}:{call.Method}");

        try
        {
            run();
        }
        catch (WasmtimeException waEx)
        {
            if (waEx.InnerException is ExitException eEx)
            {
                exitCode = eEx.ExitCode;
            }
            else
            {
                Console.WriteLine(waEx);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        Console.WriteLine($"{contract.Name}:{call.Method} return with exit code {exitCode}");

        if (exitCode != 0) 
        {
            foreach (var effect in item.Effects)
            {
                if (!ctx.LedgerWalletCache.ContainsKey(effect.To.ToString())) 
                {
                    continue;
                }

                var eWallet = ctx.LedgerWalletCache[effect.To.ToString()];

                checked
                {
                    eWallet.Balance -= effect.Value;
                    contract.Balance += effect.Value;
                }
            }
            return;
        }

        var save = instance.GetAction("Save");

        if (save != null)
        {
            Console.WriteLine($"Saving state for contract {contract.Name}");
            save();
        }
    }
}

public class TakeBalanceFromSender : BaseStep<Transaction, TransactionContext>
{
    protected override void Execute(Transaction item, TransactionContext ctx)
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

        wallet.Balance = checked(wallet.Balance + item.Value);
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
    protected override void Execute(PowBlock block, BlockchainExContext ctx)
    {
        if (block.Difficulty != ctx.CurrentDifficulty) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_DIFFICULTY);
        }
    }
}

public class VerifyNonce : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx)
    {
        if (!block.VerifyNonce()) 
        {
            throw new ExecutionException(ExecutionResult.INVALID_NONCE);
        }
    }
}

public class VerifyId : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx)
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
    protected override void Execute(PowBlock block, BlockchainExContext ctx)
    {
        var lastBlock = ctx.LastBlocks.Last();

        if (!Enumerable.SequenceEqual((byte[])block.ParentHash, (byte[])lastBlock.GetHash()))
        {
            throw new ExecutionException(ExecutionResult.INVALID_PARENT_HASH);
        }
    }
}

public class VerifyTimestampPast : BaseStep<PowBlock, BlockchainExContext>
{
    protected override void Execute(PowBlock block, BlockchainExContext ctx)
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
    protected override void Execute(PowBlock block, BlockchainExContext ctx)
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
    protected override void Execute(Transaction item, TransactionContext ctx)
    {
        if (item.PublicKey == null)
        {
            throw new ExecutionException(ExecutionResult.INVALID_SENDER);
        }

        if (item.Data is null)
        {
            throw new ExecutionException(ExecutionResult.NULL_PAYLOAD);
        }

        var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
        var payload = MessagePackSerializer.Deserialize<TransactionPayload>(item.Data, lz4Options);

        if (payload.Payload is not NewContract newContract)
        {
            throw new ExecutionException(ExecutionResult.INVALID_PAYLOAD);
        }

        using var engine = new Engine(new Config()
            .WithReferenceTypes(true));

        if(Module.Validate(engine, newContract.Code) != null)
        {
            throw new ExecutionException(ExecutionResult.INVALID_CONTRACT);
        }

        var contract = new Contract(item.PublicKey.Value.ToAddress(), newContract.Name, newContract.Code);

        var ctr = ctx.BlockRepository.GetContract(contract.Address);

        if (ctr != null) 
        {
            throw new ExecutionException(ExecutionResult.DUPLICATE_CONTRACT);
        }

        ctx.BlockRepository.AddContract(contract);
    }
}
