using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using RocksDbSharp;

namespace Kryolite.Node;

public interface IStoreManager
{
    bool AddGenesis(Genesis genesis);
    bool AddView(View view, bool broadcast, bool selfCreated);
    bool AddBlock(Blocktemplate blocktemplate, bool broadcast);
    bool AddTransaction(Transaction tx, bool broadcast);
    bool AddTransaction(TransactionDto tx, bool broadcast);
    bool AddVote(Vote vote, bool broadcast);

    Genesis? GetGenesis();
    View? GetView(SHA256Hash transactionId);
    View? GetLastView();
    List<Vote> GetVotesAtHeight(long height);
    List<Transaction> GetTransactionsAfterHeight(long height, int batchSize);
    List<SHA256Hash> GetTransactionToValidate();
    List<Transaction> GetLastNTransctions(Address address, int count);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    ChainState GetChainState();
    long GetBalance(Address address);
    
    void AddTransactionBatch(IEnumerable<TransactionDto> transactions);

    /*
    bool SetChain(List<PosBlock> blocks);
    */
    void ResetChain();

    Contract? GetContract(Address address);
    List<Ledger> GetRichList(int count);
    List<Transaction> GetTransactionsForAddress(Address address);
    Transaction? GetTransactionForHash(SHA256Hash hash);
    Ledger? GetLedger(Address address);
    string? CallContractMethod(Address address, CallMethod call);
    Token? GetToken(SHA256Hash tokenId);
    List<Token> GetTokens(Address address);
    List<Token> GetContractTokens(Address contractAddress);
}
