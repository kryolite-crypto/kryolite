using Kryolite.Shared;
using Redbus.Events;

namespace Kryolite.Node.Blockchain;

public class ValidatorEnable : EventBase
{
    public ValidatorEnable(Address address)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public Address Address { get; }
}

public class ValidatorDisable : EventBase
{
    public ValidatorDisable(Address address)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public Address Address { get; }
}
