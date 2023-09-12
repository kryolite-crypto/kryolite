using System.Collections.Immutable;
using Kryolite.Shared.Blockchain;

namespace Kryolite.Shared;

public class Blocktemplate
{
    public long Height { get; set; }
    public Address To { get; set; } = Address.NULL_ADDRESS;
    public Difficulty Difficulty { get; set; }
    public SHA256Hash Nonce { get; set; } = SHA256Hash.NULL_HASH;
    public SHA256Hash Solution { get; set; } = SHA256Hash.NULL_HASH;
    public long Timestamp { get; set; }
    public SHA256Hash ParentHash { get; set; } = SHA256Hash.NULL_HASH;
    public byte[] Data = new byte[0];
    public ImmutableList<SHA256Hash> Validates { get; set; } = ImmutableList<SHA256Hash>.Empty;
}
