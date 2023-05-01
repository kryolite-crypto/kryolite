using Kryolite.Shared.Blockchain;

namespace Kryolite.Shared;

public class Blocktemplate
{
    public long Height { get; set; }
    public Difficulty Difficulty { get; set; }
    public SHA256Hash Nonce { get; set; }
    public SHA256Hash Solution { get; set; }
    public long Timestamp { get; set; }
    public SHA256Hash ParentHash { get; set; }
    public List<Transaction> Transactions { get; set; } = new List<Transaction>();
}
