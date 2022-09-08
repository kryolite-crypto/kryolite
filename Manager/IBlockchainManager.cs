namespace Marccacoin;

public interface IBlockchainManager
{
    bool AddBlock(Block block);
    long GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    SHA256Hash GetLastBlockhash();

}