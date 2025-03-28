using Kryolite.Interface;
using Kryolite.Type;

namespace Kryolite.Module.SmartContract;

public class ConsumeTokenEventArgs : IEvent
{
    public Address Contract { get; set; } = Address.NULL_ADDRESS;
    public Address Owner { get; set; } = Address.NULL_ADDRESS;
    public SHA256Hash TokenId { get; set; } = SHA256Hash.NULL_HASH;
}
