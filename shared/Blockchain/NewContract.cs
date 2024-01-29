using System.Text;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class NewContract : ITransactionPayload
{
    [Key(0)]
    public ContractManifest Manifest { get; set; }
    [Key(1)]
    public byte[] Code { get; set; }

    public NewContract(ContractManifest manifest, byte[] code)
    {
        Manifest = manifest;
        Code = code;
    }
}
