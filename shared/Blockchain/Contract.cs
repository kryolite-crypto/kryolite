using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Crypto.RIPEMD;
using MessagePack;

namespace Kryolite.Shared;

public class Contract
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public Address Address { get; set; }
    public Address Owner { get; set; }
    public string Name { get; set; }
    public ulong Balance { get; set; }
    public byte[] Code { get; set; }
    public IntPtr? EntryPoint { get; set; }
    public ContractManifest Manifest { get; set; }
    public List<ContractSnapshot> Snapshots { get; set; } = new();

    public Contract(Address owner, ContractManifest manifest, byte[] code)
    {
        Owner = owner;
        Name = manifest.Name;
        Code = code;
        Manifest = manifest;

        Address = ToAddress();
    }

    public Address ToAddress()
    {
        var bytes = Owner.Buffer.ToList();
        bytes.AddRange(Code);

        using var sha256 = SHA256.Create();
        var shaHash = sha256.ComputeHash(bytes.ToArray());

        using var ripemd = new RIPEMD160Managed();
        var ripemdHash = ripemd.ComputeHash(shaHash);

        var addressBytes = ripemdHash.ToList();
        addressBytes.Insert(0, (byte)Network.MAIN); // network (161 mainnet, 177 testnet)
        addressBytes.Insert(1, ((byte)AddressType.CONTRACT)); // type / version

        var ripemdBytes = new List<byte>(addressBytes);
        ripemdBytes.InsertRange(0, Encoding.ASCII.GetBytes(Constant.ADDR_PREFIX));

        var h1 = sha256.ComputeHash(ripemdBytes.ToArray());
        var h2 = sha256.ComputeHash(h1);

        addressBytes.InsertRange(addressBytes.Count, h2.Take(4)); // checksum

        return addressBytes.ToArray();
    }
}

public class ContractSnapshot
{
    public Guid Id { get; set; }
    public long Height { get; set; }
    public byte[] Snapshot { get; init; }

    public ContractSnapshot(long height, byte[] snapshot)
    {
        Height = height;
        Snapshot = snapshot;
    }
}

public class ContractManifest
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyCollection<ContractMethod> Methods { get; init; } = Array.Empty<ContractMethod>();
}

public class ContractMethod
{
    public string Name { get; init; } = string.Empty;
    public bool IsReadonly { get; init; }

    [JsonPropertyName("method_params")]
    public IReadOnlyCollection<ContractParam> Params { get; init; } = Array.Empty<ContractParam>();

    [JsonPropertyName("return_value")]
    public ReturnValue Returns { get; init; } = new();
}

public class ContractParam
{
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("param_type")]
    public string Type { get; init; } = string.Empty;
}

public class ReturnValue
{
    public string value_type { get; set; }
}