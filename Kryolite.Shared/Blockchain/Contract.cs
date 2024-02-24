using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Crypto.RIPEMD;
using MemoryPack;

namespace Kryolite.Shared;

[MemoryPackable]
public partial class Contract
{
    public Address Address { get; set; }
    public Address Owner { get; set; }
    public string Name { get; set; }
    public ContractManifest Manifest { get; set; }

    [MemoryPackIgnore]
    public byte[]? CurrentSnapshot { get; set; }

    [MemoryPackConstructor]
    public Contract()
    {
        Address = Address.NULL_ADDRESS;
        Owner = Address.NULL_ADDRESS;
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
}

[MemoryPackable]
public partial class ContractManifest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("api_level")]
    public int ApiLevel { get; init; }

    [JsonPropertyName("methods")]
    public IReadOnlyCollection<ContractMethod> Methods { get; init; } = [];
}

[MemoryPackable]
public partial class ContractMethod
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("readonly")]
    public bool IsReadOnly { get; set; }

    [JsonPropertyName("method_params")]
    public IReadOnlyList<ContractParam> Params { get; init; } = [];
}

[MemoryPackable]
public partial class ContractParam
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("param_type")]
    public string Type { get; init; } = string.Empty;
}
