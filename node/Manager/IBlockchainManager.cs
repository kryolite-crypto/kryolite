using System.Numerics;
using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;

namespace Marccacoin;

public interface IBlockchainManager
{
    bool AddBlock(Block block, bool broadcastBlock = true);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    SHA256Hash GetLastBlockhash();
    BigInteger GetTotalWork();
    ulong GetBalance(Address address);
    List<WalletTransaction> GetTransactions(int count);
    Block? GetBlock(long Id);
    Wallet CreateWallet();
    List<Wallet> GetWallets();
    void UpdateWallet(Wallet wal);
    void AddTransactionsToQueue(List<Transaction> transactions);
    void AddTransactionsToQueue(Transaction transaction);
    ChainState GetChainState();
    List<Block> GetLastBlocks(int count);
    List<Block> GetLastBlocks(long start, int count);
    bool SetChain(List<Block> blocks);
    List<Block> GetFrom(long id);

    IDisposable OnBlockAdded(ITargetBlock<Block> action);
    IDisposable OnWalletUpdated(ITargetBlock<Wallet> action);
}
