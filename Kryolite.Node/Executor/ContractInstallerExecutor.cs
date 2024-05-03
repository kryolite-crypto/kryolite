using Kryolite.ByteSerializer;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class ContractInstallerExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public ContractInstallerExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx, View view)
    {
        var payload = Serializer.Deserialize<TransactionPayload>(tx.Data);

        if (payload?.Payload is not NewContract newContract)
        {
            return ExecutionResult.INVALID_PAYLOAD;
        }

        var contract = new Contract(tx.From!, newContract.Manifest, newContract.Code);

        var ctx = Context.GetRepository();
        var ctr = ctx.GetContract(contract.Address);

        if (ctr is not null)
        {
            return ExecutionResult.DUPLICATE_CONTRACT;
        }

        var vmContext = new VMContext(view, contract, tx, Context.GetRand(), Logger, 0);

        using var vm = KryoVM.LoadFromCode(newContract.Code)
            .WithContext(vmContext);

        vm.Fuel = 1_000_000;

        vm.Initialize();

        ctx.AddContract(contract);
        ctx.AddContractCode(contract.Address, newContract.Code);
        ctx.AddContractSnapshot(contract.Address, Context.GetHeight(), vm.TakeSnapshot());

        foreach (var sched in vmContext.ScheduledCalls)
        {
            Context.GetRepository().Add(sched);
        }

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
