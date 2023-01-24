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
    public string State { get; set; }

    public Contract()
    {
        Name = String.Empty;
        Code = new byte[0];
        State = String.Empty;
    }

    public Contract(Address owner, string name, byte[] code)
    {
        Owner = owner;
        Name = name;
        Code = code;
        State = String.Empty;

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
