using Kryolite.Model;

namespace Kryolite.Module.SmartContract.ABI;

public interface ICallingConv
{
    public static ICallingConv Select(Contract contract) => contract.Manifest.ApiLevel switch
    {
        0 => new CallingConv(),
        _ => throw new Exception($"Unknown CallingConv {contract.Manifest.ApiLevel}")
    };
}
