using System.ComponentModel.DataAnnotations.Schema;
using MessagePack;

namespace Kryolite.Shared;

public class LedgerWallet
{
    public Guid Id { get; set; }

    public Address Address { get; set; }
    public ulong Balance { get; set; }
    public ulong Pending { get; set; }
    // public List<LedgerAsset> Assets { get; set; } = new();


    public LedgerWallet()
    {

    }

    public LedgerWallet(Address address)
    {
        Address = address;
    }
}
