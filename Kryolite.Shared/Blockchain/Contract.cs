using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Crypto.RIPEMD;

namespace Kryolite.Shared;

public sealed class Contract : ISerializable
{
    public Address Address;
    public Address Owner;
    public string Name;
    public ContractManifest Manifest;

    public byte[]? CurrentSnapshot;

    public Contract()
    {
        Address = new();
        Owner = new();
        Name = String.Empty;
        Manifest = new();
    }

    public Contract(Address owner, ContractManifest manifest, byte[] code)
    {
        Owner = owner;
        Address = ToAddress(code);
        Manifest = manifest;
        Name = manifest.Name;
    }

    public Address ToAddress(byte[] code)
    {
        var bytes = Owner.Buffer.ToList();
        bytes.AddRange(code);

        using var sha256 = SHA256.Create();
        var shaHash = sha256.ComputeHash(bytes.ToArray());

        using var ripemd = new RIPEMD160Managed();
        var ripemdHash = ripemd.ComputeHash(shaHash);

        var addressBytes = ripemdHash.ToList();
        addressBytes.Insert(0, (byte)AddressType.CONTRACT); // type / version

        var ripemdBytes = new List<byte>(addressBytes);
        ripemdBytes.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        var h1 = sha256.ComputeHash(ripemdBytes.ToArray());
        var h2 = sha256.ComputeHash(h1);

        addressBytes.InsertRange(addressBytes.Count, h2.Take(4)); // checksum

        return (Address)addressBytes.ToArray();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CONTRACT;
    }

    public int GetLength() =>
        Serializer.SizeOf(Address) +
        Serializer.SizeOf(Owner) +
        Serializer.SizeOf(Name) +
        Serializer.SizeOf(Manifest);

    public Contract Create<Contract>() where Contract : new()
    {
        return new Contract();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Address);
        serializer.Write(Owner);
        serializer.Write(Name);
        serializer.Write(Manifest);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Address);
        serializer.Read(ref Owner);
        serializer.Read(ref Name);
        serializer.Read(ref Manifest);
    }
}

public class ContractManifest : ISerializable
{
    [JsonPropertyName("name")]
    public string Name;

    [JsonPropertyName("url")]
    public string Url;

    [JsonPropertyName("api_level")]
    public int ApiLevel;

    [JsonPropertyName("methods")]
    public List<ContractMethod> Methods;

    public ContractManifest()
    {
        Name = string.Empty;
        Url = string.Empty;
        Methods = new();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CONTRACT_MANIFEST;
    }

    public int GetLength() =>
        Serializer.SizeOf(Name) +
        Serializer.SizeOf(Url) +
        Serializer.SizeOf(ApiLevel) +
        Serializer.SizeOf(Methods);

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Name);
        serializer.Write(Url);
        serializer.Write(ApiLevel);
        serializer.Write(Methods);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Name);
        serializer.Read(ref Url);
        serializer.Read(ref ApiLevel);
        serializer.Read(ref Methods);
    }
}

public class ContractMethod : ISerializable
{
    [JsonPropertyName("name")]
    public string Name;

    [JsonPropertyName("description")]
    public string? Description;

    [JsonPropertyName("readonly")]
    public bool IsReadOnly;

    [JsonPropertyName("method_params")]
    public List<ContractParam> Params;

    public ContractMethod()
    {
        Name = string.Empty;
        Params = new();
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CONTRACT_METHOD;
    }

    public int GetLength() =>
        Serializer.SizeOf(Name) +
        Serializer.SizeOf(Description) +
        Serializer.SizeOf(IsReadOnly) +
        Serializer.SizeOf(Params);

    public ContractMethod Create<ContractMethod>() where ContractMethod : new()
    {
        return new ContractMethod();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Name);
        serializer.Write(Description);
        serializer.Write(IsReadOnly);
        serializer.Write(Params);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Name);
        serializer.ReadN(ref Description);
        serializer.Read(ref IsReadOnly);
        serializer.Read(ref Params);
    }
}

public class ContractParam : ISerializable
{
    [JsonPropertyName("name")]
    public string Name;

    [JsonPropertyName("description")]
    public string? Description;

    [JsonPropertyName("param_type")]
    public string Type;

    public ContractParam()
    {
        Name = string.Empty;
        Type = string.Empty;
    }

    public byte GetSerializerId()
    {
        return (byte)SerializerEnum.CONTRACT_PARAM;
    }

    public int GetLength() =>
        Serializer.SizeOf(Name) +
        Serializer.SizeOf(Description) +
        Serializer.SizeOf(Type);

    public ContractParam Create<ContractParam>() where ContractParam : new()
    {
        return new ContractParam();
    }

    public void Serialize(ref Serializer serializer)
    {
        serializer.Write(Name);
        serializer.Write(Description);
        serializer.Write(Type);
    }

    public void Deserialize(ref Serializer serializer)
    {
        serializer.Read(ref Name);
        serializer.ReadN(ref Description);
        serializer.Read(ref Type);
    }
}
