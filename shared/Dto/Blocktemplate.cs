using Kryolite.Shared.Blockchain;

namespace Kryolite.Shared;

public class Blocktemplate
{
    public long Height { get; set; }
    public Address To { get; set; }
    public Difficulty Difficulty { get; set; }
    public SHA256Hash Nonce { get; set; } = new SHA256Hash();
    public SHA256Hash Solution { get; set; } = new SHA256Hash();
    public long Timestamp { get; set; }
    public SHA256Hash ParentHash { get; set; } = new SHA256Hash();
    public byte[] Data = new byte[0];
    public List<SHA256Hash> Validates { get; set; } = new List<SHA256Hash>();
}
