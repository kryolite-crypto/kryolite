using System.Text.Json.Serialization;
using Kryolite.EventBus;
using Kryolite.Shared;
using Kryolite.Type;

namespace Kryolite.Node;

public class GenericEventArgs : EventBase
{
    public Address Contract { get; set; } = Address.NULL_ADDRESS;
    public List<object> EventData { get; set; } = [];
}
