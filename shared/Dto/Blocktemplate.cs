using System.Collections.Immutable;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Shared;

public class Blocktemplate
{
    public long Height { get; set; }
    public Address To { get; set; } = Address.NULL_ADDRESS;
    public ulong Value { get; set; }
    public Difficulty Difficulty { get; set; }
    public SHA256Hash Nonce { get; set; } = SHA256Hash.NULL_HASH;
    public SHA256Hash Solution { get; set; } = SHA256Hash.NULL_HASH;
    public long Timestamp { get; set; }
    public SHA256Hash ParentHash { get; set; } = SHA256Hash.NULL_HASH;
}
