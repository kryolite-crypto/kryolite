using Kryolite.Shared;
using Redbus.Events;

namespace Kryolite.Node;

public class ConsumeTokenEventArgs : EventBase
{
    public Address Owner { get; set; } = new();
    public SHA256Hash TokenId { get; set; } = new();
}
