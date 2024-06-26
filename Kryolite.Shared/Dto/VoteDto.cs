using Kryolite.Shared.Blockchain;
using Kryolite.Type;

namespace Kryolite.Shared.Dto;

public class VoteDto(Vote vote)
{
    public SHA256Hash ViewHash { get; set; } = vote.ViewHash;
    public PublicKey PublicKey { get; set; } = vote.PublicKey;
    public Address Address { get; set; } = vote.PublicKey.ToAddress();
    public Signature Signature { get; set; } = vote.Signature;
    public ulong Stake { get; set; } = vote.Stake;
}
