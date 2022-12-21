using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Kryolite.Shared;

namespace Kryolite.Node;

public interface IBlockchainManager
{
    bool AddBlock(PosBlock block, bool broadcastBlock, bool broadcastVote);
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
    bool AddVote(Vote vote);

    IDisposable OnBlockAdded(ITargetBlock<PosBlock> action);
    IDisposable OnWalletUpdated(ITargetBlock<Wallet> action);
    IDisposable OnVoteAdded(ITargetBlock<Vote> action);
}
