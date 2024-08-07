using Kryolite.EventBus;
using Kryolite.RocksDb;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Kryolite.Shared.Dto;
using Kryolite.Type;

namespace Kryolite.Interface;

public interface IStoreManager
{
    bool BlockExists(SHA256Hash blockhash);
    bool VoteExists(SHA256Hash votehash);
    bool TransactionExists(SHA256Hash hash);
    bool AddGenesis(View view);
    bool AddView(View view, bool broadcast, bool castVote);
    bool AddBlock(Block block, bool broadcast);
    bool AddBlock(BlockTemplate blocktemplate, bool broadcast);
    bool AddVote(Vote vote, bool broadcast);
    ExecutionResult AddTransaction(TransactionDto tx, bool broadcast);

    View? GetView(long id);
    View? GetView(SHA256Hash viewHash);
    View? GetLastView();
    Block? GetBlock(SHA256Hash blockhash);
    Vote? GetVote(SHA256Hash blockhash);
    List<Block> GetBlocks(List<SHA256Hash> blockhashes);
    List<Vote> GetVotes(List<SHA256Hash> votehashes);
    List<Transaction> GetTransactions(List<SHA256Hash> transactionIds);
    List<Vote> GetVotesAtHeight(long height);
    List<Transaction> GetVotesForAddress(Address address, int count);
    ICollection<Block> GetPendingBlocks();
    ICollection<Vote> GetPendingVotes();
    ICollection<Transaction> GetPendingTransactions();
    List<Transaction> GetTransactions(int pageNum, int pageSize);
    List<Transaction> GetLastNTransctions(Address address, int count);
    BlockTemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    ChainState GetChainState();
    ChainState? GetChainState(long height);
    ulong GetBalance(Address address);
    void ResetChain();
    Contract? GetContract(Address address);
    byte[]? GetContractCode(Address address);
    byte[]? GetContractSnapshot(Address address);
    List<Contract> GetContracts();
    List<Ledger> GetRichList(int count);
    List<Transaction> GetTransactionsForAddress(Address address);
    List<Transaction> GetTransactionsAtHeight(long height);
    Transaction? GetTransactionForHash(SHA256Hash hash);
    Ledger? GetLedger(Address address);
    string? CallContractMethod(Address address, CallMethod call, out ulong gasFee);
    Token? GetToken(Address contract, SHA256Hash tokenId);
    List<Token> GetTokens(Address address);
    List<Token> GetContractTokens(Address contractAddress);
    Validator? GetStake(Address address);
    List<Validator> GetValidators();
    bool LoadStagingChain(string storeName, ChainState newChain, IStateCache newState, List<EventBase> events);
    Checkpoint CreateCheckpoint();
    ulong GetEstimatedStakeReward(Address address, long milestoneId);
    long GetLastHeightContainingBlock();
    ulong GetTransactionFeeEstimate(Transaction tx);
    bool IsValidator(Address address);
}
