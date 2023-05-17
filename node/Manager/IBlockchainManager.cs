using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Node;

public interface IBlockchainManager
{
    bool AddGenesis(Genesis genesis);
    bool AddView(View view);
    View? GetLastView();
    List<Transaction> GetTransactionToValidate();

    /*bool AddBlock(PosBlock block, bool broadcastBlock, bool broadcastVote);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    SHA256Hash? GetLastBlockhash();
    BigInteger GetTotalWork();
    ulong GetBalance(Address address);
    PowBlock? GetPowBlock(long Id);
    PosBlock? GetPosBlock(long Id);
    List<Transaction> AddTransactionsToQueue(IList<Transaction> transactions, bool broadcast = true);
    void AddTransactionsToQueue(Transaction transaction);
    ChainState GetChainState();
    List<PowBlock> GetLastBlocks(int count);
    List<PowBlock> GetLastBlocks(long start, int count);
    bool SetChain(List<PosBlock> blocks);
    List<PosBlock> GetPosFrom(long id);
    List<PowBlock> GetPowFrom(long id);
    bool AddVote(Vote vote);
    List<Vote> AddVotes(IList<Vote> votes);
    void ResetChain();
    Contract? GetContract(Address address);
    List<LedgerWallet> GetRichList(int count);
    List<Transaction> GetTransactionsForAddress(Address address);
    Transaction? GetTransactionForHash(SHA256Hash hash);
    LedgerWallet? GetLedgerWallet(Address address);
    string? CallContractMethod(Address address, CallMethod call);
    Token? GetToken(SHA256Hash tokenId);
    List<Token> GetTokens(Address address);
    List<Token> GetContractTokens(Address contractAddress);*/

    IDisposable OnChainUpdated(ITargetBlock<ChainState> action);
    IDisposable OnBlockAdded(ITargetBlock<Block> action);
    IDisposable OnWalletUpdated(ITargetBlock<Wallet> action);
    IDisposable OnVoteAdded(ITargetBlock<Vote> action);
    IDisposable OnTokenTransferred(ITargetBlock<TransferTokenEventArgs> action);
    IDisposable OnTokenConsumed(ITargetBlock<ConsumeTokenEventArgs> action);
}
