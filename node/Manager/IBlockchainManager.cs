using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;

namespace Kryolite.Node;

public interface IBlockchainManager
{
    bool AddGenesis(Genesis genesis);
    bool AddView(View view, bool broadcast);
    bool AddBlock(Blocktemplate blocktemplate, bool broadcast);
    bool AddTransaction(Transaction tx, bool broadcast);
    bool AddTransaction(TransactionDto tx, bool broadcast);
    bool AddVote(Vote vote, bool broadcast);

    Genesis? GetGenesis();
    View? GetView(SHA256Hash transactionId);
    View GetLastView();
    List<Transaction> GetTransactionsAfterHeight(long height, int batchSize);
    List<Transaction> GetTransactionToValidate();
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    ChainState GetChainState();
    ulong GetBalance(Address address);
    
    void AddTransactionBatch(IList<TransactionDto> transactions);
    void AddVoteBatch(IList<Vote> votes);

    /*
    bool SetChain(List<PosBlock> blocks);
    */
    void ResetChain();

    Contract? GetContract(Address address);
    List<LedgerWallet> GetRichList(int count);
    List<Transaction> GetTransactionsForAddress(Address address);
    Transaction? GetTransactionForHash(SHA256Hash hash);
    LedgerWallet? GetLedgerWallet(Address address);
    string? CallContractMethod(Address address, CallMethod call);
    Token? GetToken(SHA256Hash tokenId);
    List<Token> GetTokens(Address address);
    List<Token> GetContractTokens(Address contractAddress);
}
