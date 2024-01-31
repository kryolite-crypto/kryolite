using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Kryolite.Node.Executor;

public class ContractExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public ContractExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx, View view)
    {
        try
        {
            var contract = Context.GetContract(tx.To);

            if (contract is null)
            {
                return ExecutionResult.INVALID_CONTRACT;
            }

            var contractLedger = Context.GetOrNewWallet(tx.To);

            if (contract.CurrentSnapshot is null)
            {
                contract.CurrentSnapshot = Context.GetRepository().GetLatestSnapshot(contract.Address);

                if (contract.CurrentSnapshot is null)
                {
                    return ExecutionResult.CONTRACT_SNAPSHOT_MISSING;
                }
            }

            var payload = MemoryPackSerializer.Deserialize<TransactionPayload>(tx.Data);

            if (payload?.Payload is not CallMethod call)
            {
                return ExecutionResult.INVALID_PAYLOAD;
            }

            var methodName = $"{call.Method}";
            var method = contract.Manifest.Methods
                .Where(x => x.Name == methodName)
                .FirstOrDefault();

            if (method == null)
            {
                return ExecutionResult.INVALID_METHOD;
            }

            var methodParams = new List<object>();

            if (call.Params is not null)
            {
                methodParams.AddRange(call.Params);
            }

            var vmContext = new VMContext(view, contract, tx, Context.GetRand(), Logger, contractLedger.Balance);

            var code = Context.GetRepository().GetContractCode(contract.Address);

            using var vm = KryoVM.LoadFromSnapshot(code, contract.CurrentSnapshot)
                .WithContext(vmContext);

            Logger.LogDebug("Executing contract {contractName}:{methodName}", contract.Name, call.Method);
            var ret = vm.CallMethod(methodName, [.. methodParams], out _);
            Logger.LogDebug("Contract result = {result}", ret);

            if (ret != 0)
            {
                tx.Effects.Clear();
                return ExecutionResult.CONTRACT_EXECUTION_FAILED;
            }

            const string getTokenName = "get_token";
            var hasGetToken = contract.Manifest.Methods.Any(x => x.Name == getTokenName);

            foreach (var effect in tx.Effects)
            {
                var wallet = Context.GetOrNewWallet(effect.To);

                if (hasGetToken && effect.TokenId is not null)
                {
                    var token = Context.GetToken(contract.Address, effect.TokenId);

                    if (token is null)
                    {
                        var result = vm.CallMethod(getTokenName, [effect.TokenId], out var json);

                        if (result != 0)
                        {
                            Logger.LogDebug("get_token failed for {tokenId}, error code = {result}", effect.TokenId, result);
                            continue;
                        }

                        if (json is null)
                        {
                            Logger.LogDebug("get_token failed for {tokenId}, error = json output null", effect.TokenId);
                            continue;
                        }

                        var tokenBase = JsonSerializer.Deserialize<TokenBase>(json);

                        if (tokenBase is null)
                        {
                            Logger.LogDebug("get_token failed for {tokenId}, error = failed to parse json", effect.TokenId);
                            continue;
                        }

                        token = new Token()
                        {
                            TokenId = effect.TokenId,
                            Ledger = wallet.Address,
                            Name = tokenBase.Name,
                            Description = tokenBase.Description,
                            Contract = contract.Address
                        };

                        Context.AddToken(token);
                    }

                    token.Ledger = wallet.Address;
                    token.IsConsumed = effect.ConsumeToken;
                }

                if (!Context.Transfer.From(tx.To, effect.Value, out var executionResult, out _))
                {
                    return executionResult;
                }

                Context.Transfer.To(effect.To, effect.Value, out _);
            }

            foreach (var sched in vmContext.ScheduledCalls)
            {
                Context.GetRepository().Add(sched);
            }

            Context.AddEvents(vmContext.Events);

            contract.CurrentSnapshot = vm.TakeSnapshot();

            return ExecutionResult.SUCCESS;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Contract failed");
            return ExecutionResult.CONTRACT_EXECUTION_FAILED;
        }
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
