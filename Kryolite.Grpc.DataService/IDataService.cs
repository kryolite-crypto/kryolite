using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Grpc.DataService;

public interface IDataService
{
    ExecutionResult AddTransaction(TransactionDto transaction);

    Validator? GetValidator(Address address);

    bool PostSolution(BlockTemplate blockTemplate);

    IAsyncEnumerable<BlockTemplate> SubscribeToBlockTemplates(Address address, CancellationToken token);
}
