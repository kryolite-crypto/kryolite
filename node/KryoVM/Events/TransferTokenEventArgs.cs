using Kryolite.EventBus;
using Kryolite.Shared;

namespace Kryolite.Node;

public class TransferTokenEventArgs : EventBase
{
    public Address Contract { get; set; } = new();
    public Address From { get; set; } = new();
    public Address To { get; set; } = new();
    public SHA256Hash TokenId { get; set; } = new();
}
