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
    List<Transaction> GetTransactions(Address address, int count);
    Block GetBlock(long Id);
}