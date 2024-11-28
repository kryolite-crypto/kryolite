using Kryolite.Model;
using Wasmtime;

namespace Kryolite.Module.SmartContract.ABI;

public interface IStandardApi
{
    void Register(Linker linker, Store store);

    public static IStandardApi Select(Contract contract) => contract.Manifest.ApiLevel switch
    {
        0 => new ApiV1(),
        _ => throw new Exception($"Unknown ApiLevel {contract.Manifest.ApiLevel}")
    };
}
