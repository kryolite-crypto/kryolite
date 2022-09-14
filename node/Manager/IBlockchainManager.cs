using Marccacoin.Shared;

namespace Marccacoin;

public interface IBlockchainManager
{
    bool AddBlock(Block block);
    Blocktemplate GetBlocktemplate(Address wallet);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    SHA256Hash GetLastBlockhash();

}