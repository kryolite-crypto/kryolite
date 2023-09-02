using System.Data.Common;
using MessagePack;
using Redbus.Events;

namespace Kryolite.Shared;

[MessagePackObject]
public class Ledger : EventBase
{
    [Key(0)]
    public long Id { get; set; }
    [Key(1)]
    public Address Address { get; set; }
    [Key(2)]
    public long Balance { get; set; }
    [Key(3)]
    public List<Token> Tokens { get; set; } = new();
    [IgnoreMember]
    public long Pending { get; set; }

    public Ledger()
    {
        Address = new Address();
    }

    public Ledger(Address address)
    {
        Address = address;
    }
}
