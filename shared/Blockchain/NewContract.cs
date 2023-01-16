using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class NewContract
{
    [Key(0)]
    public string Name { get; set; }
    [Key(1)]
    public byte[] Code { get; set; }

    public NewContract(string name, byte[] code)
    {
        Name = name;
        Code = code;
    }
}
