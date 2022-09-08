namespace Marccacoin;

public interface IBlockchainRepository
{
    long Count();
    void Add(Block block, ChainState chainState);
    public Block GetBlock(long id);
    public ChainState GetChainState();
    public List<Block> Tail(int count);
    public Block Last();
}
