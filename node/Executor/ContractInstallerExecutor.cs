using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node.Executor;

public class ContractInstallerExecutor : IExecutor
{
    private IExecutorContext Context { get; }
    private ILogger Logger { get; }

    public ContractInstallerExecutor(IExecutorContext context, ILogger logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ExecutionResult Execute(Transaction tx)
    {
        var lz4Options = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithOmitAssemblyVersion(true);

        var payload = MessagePackSerializer.Deserialize<TransactionPayload>(tx.Data, lz4Options);

        if (payload.Payload is not NewContract newContract)
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

        var vmContext = new VMContext(contract, tx, Context.GetRand(), Logger);

        using var vm = KryoVM.LoadFromCode(newContract.Code)
            .WithContext(vmContext);

        contract.EntryPoint = vm.Initialize();

        ctx.AddContract(contract);
        ctx.AddContractCode(contract.Address, newContract.Code);
        ctx.AddContractSnapshot(contract.Address, Context.GetHeight(), vm.TakeSnapshot());

        return ExecutionResult.SUCCESS;
    }

    public void Rollback(Transaction tx)
    {
        throw new NotImplementedException();
    }
}