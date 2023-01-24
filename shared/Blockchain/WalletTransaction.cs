using System.Text.Json.Serialization;

namespace Kryolite.Shared;

public class WalletTransaction
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public long Height { get; set; }
    public Address Recipient { get; set; }
    public long Value { get; set; }
    public long Timestamp { get; set; }
}
