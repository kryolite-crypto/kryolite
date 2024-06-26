using Kryolite.Shared.Blockchain;
using Kryolite.Type;

namespace Kryolite.Shared.Dto;

public class ViewDto(View view)
{
    public long Id { get; set; } = view.Id;
    public long Timestamp { get; set; } = view.Timestamp;
    public SHA256Hash LastHash { get; set; } = view.LastHash;
    public PublicKey PublicKey { get; set; } = view.PublicKey;
    public Address From { get; set; } = view.PublicKey.ToAddress();
    public Signature Signature { get; set; } = view.Signature;
    public List<SHA256Hash> Blocks { get; set; } = view.Blocks;
    public List<SHA256Hash> Transactions { get; set; } = view.Transactions;
    public List<SHA256Hash> Votes { get; set; } = view.Votes;
    public List<SHA256Hash> Rewards { get; set; } = view.Rewards;
}
