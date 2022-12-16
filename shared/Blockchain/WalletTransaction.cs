using LiteDB;

namespace Kryolite.Shared;

public class WalletTransaction
{
    [BsonId]
    public int Id { get; set; }
    public long BlockId { get; set; }
    public Address Recipient { get; set; }
    public long Value { get; set; }
    public long Timestamp { get; set; }
}
