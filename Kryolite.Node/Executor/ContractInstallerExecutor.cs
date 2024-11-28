using Kryolite.FastSerializer;
using Kryolite.Model;
using Kryolite.Module.SmartContract;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class ContractInstallerExecutor
{
    private readonly IExecutorContext _context;
    private readonly IVirtualMachineFactory _vmFactory;
    private readonly ILogger _logger;

    public ContractInstallerExecutor(IExecutorContext context, IVirtualMachineFactory vmFactory, ILogger logger)
    {
        _context = context;
        _vmFactory = vmFactory;
        _logger = logger;
    }

    public ExecutionResult Execute(Transaction tx, View view)
    {
        var payload = Serializer.Deserialize<TransactionPayload>(tx.Data);

        if (payload?.Payload is not NewContract newContract)
        {
            return ExecutionResult.INVALID_PAYLOAD;
        }

        var contract = new Contract(tx.From!, newContract.Manifest, newContract.Code);

        var ctx = _context.GetRepository();
        var ctr = ctx.GetContract(contract.Address);

        if (ctr is not null)
        {
            return ExecutionResult.DUPLICATE_CONTRACT;
        }

        var vmContext = new Context(contract, tx, view, _context.GetRand(), 0);
        var vm = _vmFactory.Create(vmContext, newContract.Code);

        vm.Fuel = 1_000_000;
        vm.Initialize();

        ctx.AddContract(contract, view.Id);
        ctx.AddContractCode(contract.Address, view.Id, newContract.Code);
        ctx.AddContractSnapshot(contract.Address, _context.GetHeight(), vm.TakeSnapshot());

        foreach (var sched in vmContext.ScheduledCalls)
        {
            _context.GetRepository().Add(sched);
        }

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}
