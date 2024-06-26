using System.Text.Json.Serialization;
using Kryolite.EventBus;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node;

public class ApprovalEventArgs : EventBase
{
    public Address Contract { get; set; } = Address.NULL_ADDRESS;
    public Address From { get; set; } = Address.NULL_ADDRESS;
    public Address To { get; set; } = Address.NULL_ADDRESS;
    public SHA256Hash TokenId { get; set; } = SHA256Hash.NULL_HASH;
}
