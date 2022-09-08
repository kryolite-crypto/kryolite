namespace Marccacoin;

public interface IBlockchainManager
{
    bool AddBlock(Block block);
    ulong GetCurrentHeight();
    Difficulty GetCurrentDifficulty();
    SHA256Hash GetLastBlockhash();

}