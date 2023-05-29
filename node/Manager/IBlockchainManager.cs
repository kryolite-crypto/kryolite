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
    bool AddTransaction(TransactionDto tx, bool broadcast);
    bool AddVote(Vote vote, bool broadcast);

    View? GetView(SHA256Hash transactionId);
    View GetLastView();
    List<Transaction> GetTransactionsAfterHeight(long height, int batchSize);
    List<Transaction> GetTransactionToValidate();
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    ChainState GetChainState();
    ulong GetBalance(Address address);
    //BigInteger GetTotalWork();
    /*SHA256Hash? GetLastBlockhash();
    List<Transaction> AddTransactionsToQueue(IList<Transaction> transactions, bool broadcast = true);
    void AddTransactionsToQueue(Transaction transaction);
    List<PowBlock> GetLastBlocks(int count);
    List<PowBlock> GetLastBlocks(long start, int count);
    bool SetChain(List<PosBlock> blocks);
    List<PosBlock> GetPosFrom(long id);
    List<PowBlock> GetPowFrom(long id);
    bool AddVote(Vote vote);
    List<Vote> AddVotes(IList<Vote> votes);
    void ResetChain();*/
    Contract? GetContract(Address address);
    List<LedgerWallet> GetRichList(int count);
    List<Transaction> GetTransactionsForAddress(Address address);
    Transaction? GetTransactionForHash(SHA256Hash hash);
    LedgerWallet? GetLedgerWallet(Address address);
    string? CallContractMethod(Address address, CallMethod call);
    Token? GetToken(SHA256Hash tokenId);
    List<Token> GetTokens(Address address);
    List<Token> GetContractTokens(Address contractAddress);

    IDisposable OnChainUpdated(ITargetBlock<ChainState> action);
    IDisposable OnWalletUpdated(ITargetBlock<Wallet> action);
    IDisposable OnVoteAdded(ITargetBlock<Vote> action);
    IDisposable OnTokenTransferred(ITargetBlock<TransferTokenEventArgs> action);
    IDisposable OnTokenConsumed(ITargetBlock<ConsumeTokenEventArgs> action);
}
