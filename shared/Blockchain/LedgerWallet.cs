using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using MessagePack;

namespace Kryolite.Shared;

public class LedgerWallet
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Address Address { get; set; }
    public ulong Balance { get; set; }
    public ulong Pending { get; set; }
    public List<Token> Tokens { get; set; } = new();


    public LedgerWallet()
    {
        Address = new Address();
    }

    public LedgerWallet(Address address)
    {
        Address = address;
    }
}
