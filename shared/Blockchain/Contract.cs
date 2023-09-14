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
    public string Name { get; set; }
    [Key(3)]
    public long Balance { get; set; }
    [Key(4)]
    public int? EntryPoint { get; set; }
    [Key(5)]
    public ContractManifest Manifest { get; set; }

    [IgnoreMember]
    public byte[]? CurrentSnapshot { get; set; }

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
        Name = manifest.Name;
        Manifest = manifest;

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
    public string Name { get; init; } = string.Empty;
    [Key(1)]
    public IReadOnlyCollection<ContractMethod> Methods { get; init; } = Array.Empty<ContractMethod>();
}

[MessagePackObject]
public class ContractMethod
{
    [Key(0)]
    public string Name { get; init; } = string.Empty;
    [Key(1)]
    [JsonPropertyName("readonly")]
    public bool IsReadonly { get; init; }

    [Key(2)]
    [JsonPropertyName("method_params")]
    public IReadOnlyCollection<ContractParam> Params { get; init; } = Array.Empty<ContractParam>();

    [Key(3)]
    [JsonPropertyName("return_value")]
    public ReturnValue Returns { get; init; } = new();
}

[MessagePackObject]
public class ContractParam
{
    [Key(0)]
    public string Name { get; init; } = string.Empty;

    [Key(1)]
    [JsonPropertyName("param_type")]
    public string Type { get; init; } = string.Empty;
}

[MessagePackObject]
public class ReturnValue
{
    [Key(0)]
    public string Type { get; set; } = string.Empty;
}
