using Kryolite.Interface;
using Kryolite.Type;

namespace Kryolite.Module.SmartContract;

public class ApprovalEventArgs : IEvent
{
    public Address Contract { get; set; } = Address.NULL_ADDRESS;
    public Address From { get; set; } = Address.NULL_ADDRESS;
    public Address To { get; set; } = Address.NULL_ADDRESS;
    public SHA256Hash TokenId { get; set; } = SHA256Hash.NULL_HASH;
}
