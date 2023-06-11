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
    public long Pending { get; set; }
    [Key(4)]
    public List<Token> Tokens { get; set; } = new();
    // public bool IsNew { get; set; }

    public Ledger()
    {
        Address = new Address();
    }

    public Ledger(Address address)
    {
        Address = address;
        // IsNew = true;
    }

    public static Ledger Read(DbDataReader reader)
    {
        return new Ledger
        {
            Address = reader.GetString(0),
            Balance = reader.GetInt64(1),
            Pending = reader.GetInt64(2)
        };
    }
}
