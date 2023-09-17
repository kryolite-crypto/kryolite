using Kryolite.EventBus;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class VMContext
{
    public Contract Contract { get; set; }
    public Transaction Transaction { get; set; }
    public ILogger Logger { get; }
    public Random Rand { get; set; }
    public List<object> EventData { get; set; } = new ();
    public long Balance { get; set; }
    public string? Returns { get; set; }
    public List<EventBase> Events { get; set; } = new ();

    public VMContext(Contract contract, Transaction transaction, Random rand, ILogger logger)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Rand = rand ?? throw new ArgumentNullException(nameof(Rand));
        Balance = (long)Contract.Balance;
    }
}
