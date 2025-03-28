using Kryolite.Model;

namespace Kryolite.Module.SmartContract;

public class Context : IContext
{
    public Contract Contract { get; }
    public Transaction Transaction { get; }
    public View View { get; }
    public long Balance { get; set; }
    public string ReturnValue { get; set; } = string.Empty;
    public Random Rand { get; }
    public List<string> EventData { get; set; } = new ();
    public List<IEvent> Events { get; set; } = new ();
    public List<string> MethodParams { get; }= new ();
    public List<Transaction> ScheduledCalls { get; } = new ();

    public Context(Contract contract, Transaction transaction, View view, Random rand, long balance)
    {
        Contract = contract;
        Transaction = transaction;
        View = view;
        Rand = rand;
        Balance = balance;
    }
}
