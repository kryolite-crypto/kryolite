using Kryolite.Node;
using Kryolite.Shared;

namespace node;

public class VMContext
{
    public Contract Contract { get; set; }
    public Transaction Transaction { get; set; }
    public Random Rand { get; set; }
    public List<object> EventData { get; set; } = new ();
    public long Balance { get; set; }
    public string? Returns { get; set; }

    public VMContext(Contract contract, Transaction transaction, int seed)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        Rand = new Random(seed);
        Balance = (long)Contract.Balance;
    }
}
