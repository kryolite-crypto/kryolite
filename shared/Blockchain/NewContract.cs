using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class NewContract : ITransactionPayload
{
    [Key(0)]
    public byte[] Code { get; set; }

    public NewContract(byte[] code)
    {
        Code = code;
    }
}
