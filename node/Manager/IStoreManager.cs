using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.EventBus;
using Kryolite.Node.Blockchain;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using QuikGraph;
using RocksDbSharp;

namespace Kryolite.Node;

public interface IStoreManager
{
    bool Exists(SHA256Hash hash);
    bool AddGenesis(Genesis genesis, View view);
    bool AddView(View view, bool broadcast, bool castVote, bool isGenesis = false);
    bool AddBlock(Blocktemplate blocktemplate, bool broadcast);
    bool AddVote(Vote vote, bool broadcast);
    ExecutionResult AddValidatorReg(TransactionDto txDto, bool broadcast);
    ExecutionResult AddTransaction(TransactionDto tx, bool broadcast);

    Genesis? GetGenesis();
    View? GetView(SHA256Hash transactionId);
    View? GetLastView();
    List<Vote> GetVotesAtHeight(long height);
    ICollection<Transaction> GetPendingTransactions();
    List<Transaction> GetTransactionsAfterHeight(long height);
    List<Transaction> GetTransactionsAtHeight(long height);
    List<Transaction> GetTransactions(int pageNum, int pageSize);
    List<SHA256Hash> GetTransactionToValidate();
    List<SHA256Hash> GetTransactionToValidate(int count);
    List<Transaction> GetLastNTransctions(Address address, int count);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    ChainState GetChainState();
    ChainState? GetChainStateAt(long height);
    long GetBalance(Address address);

    bool AddTransactionBatch(List<TransactionDto> transactions, bool broadcast);
    void ResetChain();

    Contract? GetContract(Address address);
    List<Ledger> GetRichList(int count);
    List<Transaction> GetTransactionsForAddress(Address address);
    Transaction? GetTransactionForHash(SHA256Hash hash);
    Ledger? GetLedger(Address address);
    string? CallContractMethod(Address address, CallMethod call);
    Token? GetToken(Address contract, SHA256Hash tokenId);
    List<Token> GetTokens(Address address);
    List<Token> GetContractTokens(Address contractAddress);
    Validator? GetStake(Address address);
    List<Validator> GetValidators();
    bool LoadStagingChain(string storeName, ChainState newChain, IStateCache newState, List<EventBase> events);
}
