using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Kryolite.Node.Executor;

public class ContractExecutor : IExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public ContractExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
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

            var lz4Options = MessagePackSerializerOptions.Standard
                    .WithCompression(MessagePackCompression.Lz4BlockArray)
                    .WithOmitAssemblyVersion(true);

            var payload = MessagePackSerializer.Deserialize<TransactionPayload>(tx.Data, lz4Options);

            if (payload.Payload is not CallMethod call)
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

            if (contract.EntryPoint is null)
            {
                return ExecutionResult.CONTRACT_ENTRYPOINT_MISSING;
            }

            var methodParams = new List<object> { contract.EntryPoint };

            if (call.Params is not null)
            {
                methodParams.AddRange(call.Params);
            }

            var vmContext = new VMContext(contract, tx, Context.GetRand(), Logger, contractLedger.Balance);

            var code = Context.GetRepository().GetContractCode(contract.Address);

            using var vm = KryoVM.LoadFromSnapshot(code, contract.CurrentSnapshot)
                .WithContext(vmContext);

            Logger.LogDebug($"Executing contract {contract.Name}:{call.Method}");
            var ret = vm.CallMethod(methodName, methodParams.ToArray(), out _);
            Logger.LogDebug($"Contract result = {ret}");

            if (ret != 0)
            {
                tx.Effects.Clear();
                return ExecutionResult.CONTRACT_EXECUTION_FAILED;
            }

            var getTokenName = $"get_token";
            var hasGetToken = contract.Manifest.Methods.Any(x => x.Name == getTokenName);

            foreach (var effect in tx.Effects)
            {
                var wallet = Context.GetOrNewWallet(effect.To);

                if (hasGetToken && effect.TokenId is not null)
                {
                    var token = Context.GetToken(contract.Address, effect.TokenId);

                    if (token is null)
                    {
                        var result = vm.CallMethod(getTokenName, new object[] { contract.EntryPoint, effect.TokenId }, out var json);

                        if (result != 0)
                        {
                            Logger.LogDebug($"get_token failed for {effect.TokenId}, error code = {result}");
                            continue;
                        }

                        if (json is null)
                        {
                            Logger.LogDebug($"get_token failed for {effect.TokenId}, error = json output null");
                            continue;
                        }

                        var tokenBase = JsonSerializer.Deserialize<TokenBase>(json);

                        if (tokenBase is null)
                        {
                            Logger.LogDebug($"get_token failed for {effect.TokenId}, error = failed to parse json");
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

                checked
                {
                    wallet.Balance += effect.Value;

                    var balance = contractLedger.Balance - effect.Value;

                    if (balance < 0)
                    {
                        return ExecutionResult.TOO_LOW_BALANCE;
                    }

                    contractLedger.Balance = balance;
                }
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
