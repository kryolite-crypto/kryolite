using Kryolite.Shared;

namespace Kryolite.Node;

public class ConsumeTokenEventArgs : EventArgs
{
    public Address Owner { get; set; } = new();
    public SHA256Hash TokenId { get; set; } = new();
}
