using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Crypto.RIPEMD;
using MessagePack;

namespace Kryolite.Shared;

[MessagePackObject]
public class Contract
{
    [Key(0)]
    public Address Address { get; set; }
    [Key(1)]
    public Address Owner { get; set; }
    [Key(2)]
    public string? Name { get; set; }
    [Key(3)]
    public ContractManifest? Manifest { get; set; }

    [IgnoreMember]
    public byte[]? CurrentSnapshot { get; set; }

    public Contract()
    {
        Address = Address.NULL_ADDRESS;
        Owner = Address.NULL_ADDRESS;
        Name = String.Empty;
        Manifest = new();
    }

    public Contract(Address owner, byte[] code)
    {
        Owner = owner;
        Address = ToAddress(code);
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
        addressBytes.Insert(0, (byte)Network.MAIN); // network (161 mainnet, 177 testnet)
        addressBytes.Insert(1, (byte)AddressType.CONTRACT); // type / version

        var ripemdBytes = new List<byte>(addressBytes);
        ripemdBytes.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        var h1 = sha256.ComputeHash(ripemdBytes.ToArray());
        var h2 = sha256.ComputeHash(h1);

        addressBytes.InsertRange(addressBytes.Count, h2.Take(4)); // checksum

        return addressBytes.ToArray();
    }
}

[MessagePackObject]
public class ContractManifest
{
    [Key(0)]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    [Key(1)]
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
    [Key(2)]
    [JsonPropertyName("api_level")]
    public int ApiLevel { get; init; }
    [Key(3)]
    [JsonPropertyName("methods")]
    public IReadOnlyCollection<ContractMethod> Methods { get; init; } = [];
}

[MessagePackObject]
public class ContractMethod
{
    [Key(0)]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [Key(1)]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Key(2)]
    [JsonPropertyName("readonly")]
    public bool IsReadOnly { get; set; }

    [Key(3)]
    [JsonPropertyName("method_params")]
    public IReadOnlyCollection<ContractParam> Params { get; init; } = [];
}

[MessagePackObject]
public class ContractParam
{
    [Key(0)]
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [Key(1)]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Key(2)]
    [JsonPropertyName("param_type")]
    public string Type { get; init; } = string.Empty;
}
