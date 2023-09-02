using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using QuikGraph;
using RocksDbSharp;

namespace Kryolite.Node;

public interface IStoreManager
{
    bool Exists(SHA256Hash hash);
    bool AddGenesis(Genesis genesis);
    bool AddView(View view, bool broadcast, bool castVote, bool isGenesis = false);
    bool AddBlock(Blocktemplate blocktemplate, bool broadcast);
    bool AddVote(Vote vote, bool broadcast);
    bool AddValidatorReg(TransactionDto txDto, bool broadcast);
    ExecutionResult AddTransaction(TransactionDto tx, bool broadcast);

    Genesis? GetGenesis();
    View? GetView(SHA256Hash transactionId);
    View? GetLastView();
    List<Vote> GetVotesAtHeight(long height);
    List<Transaction> GetTransactionsAfterHeight(long height);
    List<SHA256Hash> GetTransactionToValidate();
    List<SHA256Hash> GetTransactionToValidate(int count);
    List<Transaction> GetLastNTransctions(Address address, int count);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    ChainState GetChainState();
    ChainState? GetChainStateAt(long height);
    long GetBalance(Address address);

    bool AddTransactionBatch(List<TransactionDto> transactions, bool breadcast);
    bool SetChain(AdjacencyGraph<SHA256Hash, Edge<SHA256Hash>> chainGraph, Dictionary<SHA256Hash, TransactionDto> transactions, long startHeight);
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
    Stake? GetStake(Address address);
}
