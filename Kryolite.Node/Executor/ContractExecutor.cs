using Kryolite.FastSerializer;
using Kryolite.Node.Procedure;
using Kryolite.Model;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Kryolite.Module.SmartContract;

namespace Kryolite.Node.Executor;

public class ContractExecutor(IExecutorContext _context, IVirtualMachineFactory _vmFactory, ILogger _logger)
{
    public ExecutionResult Execute(Transaction tx, View view, ref Transfer transfer)
    {
        try
        {
            var contract = _context.GetContract(tx.To);

            if (contract is null)
            {
                return ExecutionResult.INVALID_CONTRACT;
            }

            var contractLedger = _context.GetOrNewWallet(tx.To);

            if (contract.CurrentSnapshot is null)
            {
                contract.CurrentSnapshot = _context.GetRepository().GetLatestSnapshot(contract.Address);

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

            var vmContext = new Context(contract, tx, view, _context.GetRand(), (long)contractLedger.Balance);
            var vm = _vmFactory.Load(vmContext);

            var fuelStart = tx.MaxFee - tx.SpentFee;

            vm.Fuel = fuelStart;
            _logger.LogDebug("Set fuel to {fuel}", vm.Fuel);

            _logger.LogDebug("Executing contract {contractName}:{methodName}", contract.Name, call.Method);
            var ret = vm.CallMethod(methodName, call.Params, out _);
            _logger.LogDebug("Contract result = {result}, fuel burned = {fuel}", ret, fuelStart - vm.Fuel);

            tx.SpentFee += (uint)(fuelStart - vm.Fuel);

            if (ret != 0)
            {
                tx.Effects.Clear();
                return ExecutionResult.CONTRACT_EXECUTION_FAILED;
            }

            foreach (var effect in tx.Effects)
            {
                var wallet = _context.GetOrNewWallet(effect.To);

                // Handle token effect
                if (effect.TokenId is not null)
                {
                    var token = _context.GetToken(contract.Address, effect.TokenId);

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

                        _context.AddToken(token);
                    }

                    token.Ledger = effect.To;
                    token.IsConsumed = effect.ConsumeToken;
                }

                // Take funds from contract
                if (!transfer.From(contract.Address, effect.Value, out var executionResult, out _))
                {
                    // TODO: if this fails, we should rollback previous effects or else they will be only partially added
                    _logger.LogInformation("Failed to take funds from contract");
                    return ExecutionResult.CONTRACT_EXECUTION_FAILED;
                }

                // Add funds to recipient
                transfer.To(effect.To, effect.Value, out _);
            }

            foreach (var sched in vmContext.ScheduledCalls)
            {
                _context.GetRepository().Add(sched);
            }

            _context.AddEvents(vmContext.Events);

            contract.CurrentSnapshot = vm.TakeSnapshot();

            return ExecutionResult.SUCCESS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contract failed");
            return ExecutionResult.CONTRACT_EXECUTION_FAILED;
        }
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
