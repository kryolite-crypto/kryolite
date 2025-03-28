using Kryolite.Interface;
using Kryolite.Type;

namespace Kryolite.Module.SmartContract;

public class GenericEventArgs : IEvent
{
    public Address Contract { get; set; } = Address.NULL_ADDRESS;
    public List<object> EventData { get; set; } = [];
}
