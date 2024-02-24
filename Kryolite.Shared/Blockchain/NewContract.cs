using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class NewContract : ITransactionPayload
{
    public ContractManifest Manifest { get; set; }
    [BrotliFormatter]
    public byte[] Code { get; set; }

    public NewContract(ContractManifest manifest, byte[] code)
    {
        Manifest = manifest;
        Code = code;
    }
}
