using Kryolite.Shared;
using Redbus.Events;

namespace Kryolite.Node;

public class ApprovalEventArgs : EventBase
{
    public Address From { get; set; } = new();
    public Address To { get; set; } = new();
    public SHA256Hash TokenId { get; set; } = new();
}
