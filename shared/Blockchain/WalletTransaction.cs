namespace Kryolite.Shared;

public class WalletTransaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public long Height { get; set; }
    public Address Recipient { get; set; }
    public long Value { get; set; }
    public long Timestamp { get; set; }
}
