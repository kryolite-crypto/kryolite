using LiteDB;

namespace Kryolite.Shared;

public class LedgerWallet
{
    [BsonId]
    public int Id { get; set; }
    public Address Address { get; set; }
    public ulong Balance { get; set; }
    public ulong Pending { get; set; }

    public LedgerWallet(Address address) => Address = address;
}
