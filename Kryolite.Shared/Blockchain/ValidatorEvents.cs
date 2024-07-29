using Kryolite.EventBus;
using Kryolite.Type;

namespace Kryolite.Shared.Blockchain;

public class ValidatorEnable(Address address) : EventBase
{
    public Address Address { get; } = address ?? throw new ArgumentNullException(nameof(address));
}

public class ValidatorDisable(Address address) : EventBase
{
    public Address Address { get; } = address ?? throw new ArgumentNullException(nameof(address));
}
