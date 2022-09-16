using LiteDB;

namespace Marccacoin.Shared;

public class Wallet
{
    [BsonId]
    public int Id { get; set; }
    public Address Address { get; set; }
    public ulong Balance { get; set; }

    public List<Transaction> Transactions { get; set; } = new List<Transaction>();
}
