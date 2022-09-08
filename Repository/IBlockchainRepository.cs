namespace Marccacoin;

public interface IBlockchainRepository
{
    long Count();
    void Add(Block block);
    public Block Get(long id);
    public List<Block> Tail(int count);
    public Block Last();
}