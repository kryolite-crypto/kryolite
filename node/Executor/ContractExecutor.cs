﻿using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kryolite.Node.Executor;

public class ContractExecutor : IContractExecutor
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

            if (contract.CurrentSnapshot is null)
            {
                contract.CurrentSnapshot = contract.Snapshots
                    .OrderByDescending(x => x.Height)
                    .FirstOrDefault();

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

            var vmContext = new VMContext(contract, tx, Context.GetRand(), Logger);

            using var vm = KryoVM.LoadFromSnapshot(contract.Code, contract.CurrentSnapshot.Snapshot)
                .WithContext(vmContext);

            Logger.LogInformation($"Executing contract {contract.Name}:{call.Method}");
            var ret = vm.CallMethod(methodName, methodParams.ToArray(), out _);
            Logger.LogInformation($"Contract result = {ret}");

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
                    var token = Context.GetToken(effect.TokenId);

                    if (token is null)
                    {
                        var result = vm.CallMethod(getTokenName, new object[] { contract.EntryPoint, effect.TokenId }, out var json);

                        if (result != 0)
                        {
                            Logger.LogError($"get_token failed for {effect.TokenId}, error code = {result}");
                            continue;
                        }

                        if (json is null)
                        {
                            Logger.LogError($"get_token failed for {effect.TokenId}, error = json output null");
                            continue;
                        }

                        var tokenBase = JsonSerializer.Deserialize<TokenBase>(json);

                        if (tokenBase is null)
                        {
                            Logger.LogError($"get_token failed for {effect.TokenId}, error = failed to parse json");
                            continue;
                        }

                        token = new Token()
                        {
                            TokenId = effect.TokenId,
                            Name = tokenBase.Name,
                            Description = tokenBase.Description,
                            Contract = contract
                        };

                        Context.AddToken(token);
                    }

                    token.Wallet = wallet;
                    token.IsConsumed = effect.ConsumeToken;
                }

                checked
                {
                    wallet.Balance += effect.Value;

                    var balance = checked(contract.Balance - effect.Value);

                    if (balance < 0)
                    {
                        return ExecutionResult.TOO_LOW_BALANCE;
                    }

                    contract.Balance = balance;
                }
            }

            Context.AddEvents(vmContext.Events);

            contract.CurrentSnapshot = new ContractSnapshot(tx.Height ?? 0, vm.TakeSnapshot());

            return ExecutionResult.SUCCESS;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Contract failed");

            return ExecutionResult.CONTRACT_EXECUTION_FAILED;
        }
    }
}
