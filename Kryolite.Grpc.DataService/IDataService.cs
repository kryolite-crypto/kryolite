using System.ServiceModel;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Grpc.DataService;

[ServiceContract]
public interface IDataService
{
    [OperationContract]
    ExecutionResult AddTransaction(TransactionDto transaction);

    [OperationContract]
    Validator? GetValidator(Address address);

    [OperationContract]
    bool PostSolution(BlockTemplate blockTemplate);

    [OperationContract]
    IAsyncEnumerable<BlockTemplate> SubscribeToBlockTemplates(Address address, CancellationToken token);
}
