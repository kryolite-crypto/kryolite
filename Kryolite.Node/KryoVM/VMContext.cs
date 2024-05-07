using Kryolite.EventBus;
using Kryolite.Shared;
using Kryolite.Shared.Blockchain;
using Microsoft.Extensions.Logging;

namespace Kryolite.Node;

public class VMContext
{
    public View View { get; set; }
    public Contract Contract { get; set; }
    public Transaction Transaction { get; set; }
    public ILogger Logger { get; }
    public Random Rand { get; set; }
    public List<object> EventData { get; set; } = new ();
    public ulong Balance { get; set; }
    public string? Returns { get; set; }
    public List<EventBase> Events { get; set; } = new ();
    public List<string> MethodParams = new ();
    public List<Transaction> ScheduledCalls = new ();

    public VMContext(View view, Contract contract, Transaction transaction, Random rand, ILogger logger, ulong balance)
    {
        View = view ?? throw new ArgumentNullException(nameof(contract));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Rand = rand ?? throw new ArgumentNullException(nameof(Rand));
        Balance = balance;
    }
}
