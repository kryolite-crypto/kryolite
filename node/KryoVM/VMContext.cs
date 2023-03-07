using Kryolite.Node;
using Kryolite.Shared;

namespace node;

public class VMContext
{
    public Contract Contract { get; set; }
    public Transaction Transaction { get; set; }

    public VMContext(Contract contract, Transaction transaction)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }
}
