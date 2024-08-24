using System.Diagnostics.CodeAnalysis;
using Kryolite.Type;

namespace Kryolite.Module.SmartContract;

public interface ICache
{
    bool TryGetValue(Address address, [NotNullWhen(true)]out VirtualMachine? vm);
    void Set(Address address, VirtualMachine vm);
}
