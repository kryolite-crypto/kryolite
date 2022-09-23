using System.Threading.Tasks.Dataflow;
using Marccacoin.Shared;

namespace Marccacoin;

public interface IBlockchainManager
{
    bool AddBlock(Block block);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    SHA256Hash GetLastBlockhash();
    ulong GetBalance(Address address);
    List<WalletTransaction> GetTransactions(int count);
    Block GetBlock(long Id);
    Wallet CreateWallet();
    List<Wallet> GetWallets();
    void UpdateWallet(Wallet wal);
    void AddTransactionsToQueue(List<Transaction> transactions);
    void AddTransactionsToQueue(Transaction transaction);

    IDisposable OnBlockAdded(ITargetBlock<Block> action);
    IDisposable OnWalletUpdated(ITargetBlock<Wallet> action);
}
