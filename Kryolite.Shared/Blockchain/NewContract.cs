using Kryolite.ByteSerializer;

namespace Kryolite.Shared;

public sealed class NewContract : ITransactionPayload
{
    public ContractManifest Manifest;
    public byte[] Code;

    public NewContract()
    {
        Manifest = new();
        Code = [];
    }

    public NewContract(ContractManifest manifest, byte[] code)
    {
        Manifest = manifest;
        Code = code;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.NEW_CONTRACT;
    }

    public int GetLength() =>
        Serializer.SizeOf(Manifest) +
        Serializer.SizeOf(Code);

    public NewContract Create<NewContract>() where NewContract : new()
    {
        return new NewContract();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Manifest);
        serializer.Write(Code);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Manifest);
        serializer.Read(ref Code);
    }
}
