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
    Validator? GetValidator(string address);

    [OperationContract]
    bool PostSolution(BlockTemplate blockTemplate);

    [OperationContract]
    IAsyncEnumerable<BlockTemplate> SubscribeToBlockTemplates(string address, CancellationToken token);
}
