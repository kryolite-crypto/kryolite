using Kryolite.ByteSerializer;
using Kryolite.Node.Procedure;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Kryolite.Node.Executor;

public class ContractExecutor(IExecutorContext context, ILogger logger)
{
    private IExecutorContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));
    private ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

    public ExecutionResult Execute(Transaction tx, View view, ref Transfer transfer)
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

            var vm = KryoVM.LoadFromSnapshot(tx.To, Context.GetRepository(), contract.CurrentSnapshot)
                .WithContext(vmContext);

            var fuelStart = tx.MaxFee - tx.SpentFee;

            vm.Fuel = fuelStart;
            Logger.LogDebug("Set fuel to {fuel}", vm.Fuel);

            Logger.LogDebug("Executing contract {contractName}:{methodName}", contract.Name, call.Method);
            var ret = vm.CallMethod(methodName, [.. methodParams], out _);
            Logger.LogDebug("Contract result = {result}, fuel burned = {fuel}", ret, fuelStart - vm.Fuel);

            tx.SpentFee += (uint)(fuelStart - vm.Fuel);

            if (ret != 0)
            {
                tx.Effects.Clear();
                return ExecutionResult.CONTRACT_EXECUTION_FAILED;
            }

            foreach (var effect in tx.Effects)
            {
                var wallet = Context.GetOrNewWallet(effect.To);

                // Handle token effect
                if (effect.TokenId is not null)
                {
                    var token = Context.GetToken(contract.Address, effect.TokenId);

                    if (token is null)
                    {
                        // Create new token
                        token = new Token
                        {
                            TokenId = effect.TokenId,
                            Ledger = effect.To,
                            Name = effect.Name,
                            Description = effect.Description,
                            Contract = contract.Address
                        };

                        Context.AddToken(token);
                    }

                    token.Ledger = effect.To;
                    token.IsConsumed = effect.ConsumeToken;
                }

                // Take funds from contract
                if (!transfer.From(contract.Address, effect.Value, out var executionResult, out _))
                {
                    // TODO: if this fails, we should rollback previous effects or else they will be only partially added
                    Logger.LogInformation("Failed to take funds from contract");
                    return ExecutionResult.CONTRACT_EXECUTION_FAILED;
                }

                // Add funds to recipient
                transfer.To(effect.To, effect.Value, out _);
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
