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

    public ContractSnapshot(long height, ReadOnlySpan<byte> snapshot)
    {
        Height = height;
        Snapshot = snapshot.ToArray();
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
