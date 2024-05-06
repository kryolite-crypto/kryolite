using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Kryolite.Node.Executor;

public class ContractExecutor(IExecutorContext context, ILogger logger)
{
    private IExecutorContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));
    private ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

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

            var payload = Serializer.Deserialize<TransactionPayload>(tx.Data);

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

            var fuelStart = tx.MaxFee - tx.SpentFee;

            vm.Fuel = fuelStart;
            Logger.LogDebug("Set fuel to {fuel}", vm.Fuel);

            Logger.LogDebug("Executing contract {contractName}:{methodName}", contract.Name, call.Method);
            var ret = vm.CallMethod(methodName, [.. methodParams], out _);
            Logger.LogDebug("Contract result = {result}", ret);

            tx.SpentFee += (uint)(fuelStart - vm.Fuel);

            if (ret != 0)
            {
                tx.Effects.Clear();
                return ExecutionResult.CONTRACT_EXECUTION_FAILED;
            }

            foreach (var token in vmContext.Tokens)
            {
                Context.AddToken(token);
            }

            foreach (var effect in tx.Effects)
            {
                var wallet = Context.GetOrNewWallet(effect.To);

                // Handle token effect
                if (effect.TokenId is not null)
                {
                    var token = Context.GetToken(contract.Address, effect.TokenId) ?? throw new Exception($"Context is missing token {effect.TokenId}");
                    token.Ledger = wallet.Address;
                    token.IsConsumed = effect.ConsumeToken;
                }

                // Take funds from contract
                if (!Context.Transfer.From(contract.Address, effect.Value, out var executionResult, out _))
                {
                    return executionResult;
                }

                // Add funds to recipient
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
