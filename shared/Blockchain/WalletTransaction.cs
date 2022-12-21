namespace Kryolite.Shared;

public class WalletTransaction
{
    public int Id { get; set; }
    public long BlockId { get; set; }
    public Address Recipient { get; set; }
    public long Value { get; set; }
    public long Timestamp { get; set; }
    public Wallet Wallet { get; set; }
}
