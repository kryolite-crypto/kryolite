using Kryolite.Shared.Blockchain;
using Kryolite.Type;

namespace Kryolite.Shared.Dto;

public class BlockDto(Block block)
{
    public Address To { get; set; } = block.To;
    public long Timestamp { get; set; } = block.Timestamp;
    public SHA256Hash LastHash { get; set; } = block.LastHash;
    public string Difficulty { get; set; } = block.Difficulty.ToString();
    public SHA256Hash Nonce { get; set; } = block.Nonce;
    public SHA256Hash Blockhash { get; set; } = block.GetHash();
}
